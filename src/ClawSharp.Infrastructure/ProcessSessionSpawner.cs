// TS origin: ./bridge/sessionRunner.ts
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClawSharp.Bridge;

namespace ClawSharp.Infrastructure;

public sealed record ProcessSessionSpawnerDependencies(
    string ExecutablePath,
    IReadOnlyList<string>? ExecutableArgumentsPrefix = null,
    IReadOnlyDictionary<string, string?>? EnvironmentVariables = null,
    bool Verbose = false,
    bool Sandbox = false,
    string? DebugFile = null,
    string? PermissionMode = null,
    Action<string>? OnDebug = null,
    Action<string, SessionActivity>? OnActivity = null,
    Action<string, JsonObject, string>? OnPermissionRequest = null,
    ISessionChildProcessFactory? ProcessFactory = null);

public interface ISessionChildProcessFactory
{
    ISessionChildProcess Start(ProcessStartInfo startInfo);
}

public interface ISessionChildProcess
{
    int? ProcessId { get; }

    event Action<string>? OutputLineReceived;

    event Action<string>? ErrorLineReceived;

    event Action<int?>? Exited;

    void BeginReading();

    void WriteToStandardInput(string data);

    void Terminate();

    void ForceTerminate();
}

public sealed class ProcessSessionSpawner : ISessionSpawner
{
    private readonly ProcessSessionSpawnerDependencies _dependencies;

    public ProcessSessionSpawner(ProcessSessionSpawnerDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentException.ThrowIfNullOrWhiteSpace(dependencies.ExecutablePath);

        _dependencies = dependencies with
        {
            ExecutableArgumentsPrefix = dependencies.ExecutableArgumentsPrefix ?? [],
            EnvironmentVariables = dependencies.EnvironmentVariables ?? new Dictionary<string, string?>(StringComparer.Ordinal),
            ProcessFactory = dependencies.ProcessFactory ?? new SessionChildProcessFactory()
        };
    }

    public ISessionHandle Spawn(SessionSpawnOptions options, string dir)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(dir);

        var safeId = SessionRunnerUtilities.SafeFilenameId(options.SessionId);
        var debugFile = ResolveDebugFile(safeId);
        var transcriptPath = debugFile is null
            ? null
            : Path.Combine(Path.GetDirectoryName(debugFile)!, $"bridge-transcript-{safeId}.jsonl");

        if (transcriptPath is not null)
        {
            _dependencies.OnDebug?.Invoke($"[bridge:session] Transcript log: {transcriptPath}");
        }

        var arguments = BuildArguments(options, debugFile);
        var environment = BuildEnvironment(options);
        var startInfo = BuildStartInfo(dir, arguments, environment);

        _dependencies.OnDebug?.Invoke(
            $"[bridge:session] Spawning sessionId={options.SessionId} sdkUrl={options.SdkUrl} accessToken={(string.IsNullOrEmpty(options.AccessToken) ? "MISSING" : "present")}");
        _dependencies.OnDebug?.Invoke($"[bridge:session] Child args: {string.Join(' ', arguments)}");
        if (!string.IsNullOrEmpty(debugFile))
        {
            _dependencies.OnDebug?.Invoke($"[bridge:session] Debug log: {debugFile}");
        }

        var process = _dependencies.ProcessFactory!.Start(startInfo);
        _dependencies.OnDebug?.Invoke(
            $"[bridge:session] sessionId={options.SessionId} pid={process.ProcessId}");

        return new ProcessSessionHandle(options, process, transcriptPath, _dependencies);
    }

    private string? ResolveDebugFile(string safeId)
    {
        if (!string.IsNullOrEmpty(_dependencies.DebugFile))
        {
            var extensionIndex = _dependencies.DebugFile.LastIndexOf('.');
            return extensionIndex > 0
                ? $"{_dependencies.DebugFile[..extensionIndex]}-{safeId}{_dependencies.DebugFile[extensionIndex..]}"
                : $"{_dependencies.DebugFile}-{safeId}";
        }

        var userType = _dependencies.EnvironmentVariables!.TryGetValue("USER_TYPE", out var value) ? value : null;
        if (_dependencies.Verbose || string.Equals(userType, "ant", StringComparison.Ordinal))
        {
            return Path.Combine(Path.GetTempPath(), "claude", $"bridge-session-{safeId}.log");
        }

        return null;
    }

    private IReadOnlyList<string> BuildArguments(SessionSpawnOptions options, string? debugFile)
    {
        List<string> arguments = [.. _dependencies.ExecutableArgumentsPrefix!];
        arguments.Add("--print");
        arguments.Add("--sdk-url");
        arguments.Add(options.SdkUrl);
        arguments.Add("--session-id");
        arguments.Add(options.SessionId);
        arguments.Add("--input-format");
        arguments.Add("stream-json");
        arguments.Add("--output-format");
        arguments.Add("stream-json");
        arguments.Add("--replay-user-messages");

        if (_dependencies.Verbose)
        {
            arguments.Add("--verbose");
        }

        if (!string.IsNullOrEmpty(debugFile))
        {
            arguments.Add("--debug-file");
            arguments.Add(debugFile);
        }

        if (!string.IsNullOrEmpty(_dependencies.PermissionMode))
        {
            arguments.Add("--permission-mode");
            arguments.Add(_dependencies.PermissionMode);
        }

        return arguments;
    }

    private Dictionary<string, string?> BuildEnvironment(SessionSpawnOptions options)
    {
        Dictionary<string, string?> environment = new(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            environment[(string)entry.Key] = entry.Value?.ToString();
        }

        foreach (var pair in _dependencies.EnvironmentVariables!)
        {
            environment[pair.Key] = pair.Value;
        }

        environment.Remove("CLAUDE_CODE_OAUTH_TOKEN");
        environment["CLAUDE_CODE_ENVIRONMENT_KIND"] = "bridge";
        environment["CLAUDE_CODE_SESSION_ACCESS_TOKEN"] = options.AccessToken;
        environment["CLAUDE_CODE_POST_FOR_SESSION_INGRESS_V2"] = "1";

        if (_dependencies.Sandbox)
        {
            environment["CLAUDE_CODE_FORCE_SANDBOX"] = "1";
        }

        if (options.UseCcrV2 == true)
        {
            environment["CLAUDE_CODE_USE_CCR_V2"] = "1";
            environment["CLAUDE_CODE_WORKER_EPOCH"] = options.WorkerEpoch?.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return environment;
    }

    private ProcessStartInfo BuildStartInfo(
        string dir,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?> environment)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _dependencies.ExecutablePath,
            WorkingDirectory = dir,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Clear();
        foreach (var pair in environment)
        {
            if (pair.Value is null)
            {
                continue;
            }

            startInfo.Environment[pair.Key] = pair.Value;
        }

        return startInfo;
    }

    private sealed class ProcessSessionHandle : ISessionHandle
    {
        private readonly SessionSpawnOptions _options;
        private readonly ISessionChildProcess _process;
        private readonly ProcessSessionSpawnerDependencies _dependencies;
        private readonly string? _transcriptPath;
        private readonly object _gate = new();
        private readonly StreamWriter? _transcriptWriter;
        private readonly TaskCompletionSource<SessionDoneStatus> _doneSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<SessionActivity> _activities = [];
        private readonly List<string> _lastStderr = [];
        private SessionActivity? _currentActivity;
        private bool _terminated;
        private bool _forceKilled;
        private bool _firstUserMessageSeen;

        public ProcessSessionHandle(
            SessionSpawnOptions options,
            ISessionChildProcess process,
            string? transcriptPath,
            ProcessSessionSpawnerDependencies dependencies)
        {
            _options = options;
            _process = process;
            _dependencies = dependencies;
            _transcriptPath = transcriptPath;
            AccessToken = options.AccessToken;

            if (_transcriptPath is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_transcriptPath)!);
                _transcriptWriter = new StreamWriter(
                    new FileStream(_transcriptPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                    new UTF8Encoding(false))
                {
                    AutoFlush = true
                };
            }

            _process.OutputLineReceived += HandleOutputLine;
            _process.ErrorLineReceived += HandleErrorLine;
            _process.Exited += HandleExit;
            _process.BeginReading();
        }

        public string SessionId => _options.SessionId;

        public Task<SessionDoneStatus> Done => _doneSource.Task;

        public IReadOnlyList<SessionActivity> Activities => _activities;

        public SessionActivity? CurrentActivity => _currentActivity;

        public string AccessToken { get; private set; }

        public IReadOnlyList<string> LastStderr => _lastStderr;

        public void Kill()
        {
            lock (_gate)
            {
                _terminated = true;
            }

            _dependencies.OnDebug?.Invoke(
                $"[bridge:session] Sending SIGTERM to sessionId={_options.SessionId} pid={_process.ProcessId}");
            _process.Terminate();
        }

        public void ForceKill()
        {
            lock (_gate)
            {
                if (_forceKilled)
                {
                    return;
                }

                _forceKilled = true;
                _terminated = true;
            }

            _dependencies.OnDebug?.Invoke(
                $"[bridge:session] Sending SIGKILL to sessionId={_options.SessionId} pid={_process.ProcessId}");
            _process.ForceTerminate();
        }

        public void WriteStdin(string data)
        {
            _dependencies.OnDebug?.Invoke(
                $"[bridge:ws] sessionId={_options.SessionId} >>> {TruncateForDebug(data)}");
            _process.WriteToStandardInput(data);
        }

        public void UpdateAccessToken(string token)
        {
            AccessToken = token;
            WriteStdin(
                JsonSerializer.Serialize(new
                {
                    type = "update_environment_variables",
                    variables = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["CLAUDE_CODE_SESSION_ACCESS_TOKEN"] = token
                    }
                }) + "\n");
            _dependencies.OnDebug?.Invoke(
                $"[bridge:session] Sent token refresh via stdin for sessionId={_options.SessionId}");
        }

        private void HandleOutputLine(string line)
        {
            if (_transcriptWriter is not null)
            {
                _transcriptWriter.WriteLine(line);
            }

            _dependencies.OnDebug?.Invoke(
                $"[bridge:ws] sessionId={_options.SessionId} <<< {TruncateForDebug(line)}");

            if (_dependencies.Verbose)
            {
                Console.Error.WriteLine(line);
            }

            foreach (var activity in SessionRunnerUtilities.ExtractActivities(line, _options.SessionId, _dependencies.OnDebug))
            {
                if (_activities.Count >= SessionRunnerUtilities.MaxActivities)
                {
                    _activities.RemoveAt(0);
                }

                _activities.Add(activity);
                _currentActivity = activity;
                _dependencies.OnActivity?.Invoke(_options.SessionId, activity);
            }

            JsonObject? parsedObject;
            try
            {
                parsedObject = JsonNode.Parse(line) as JsonObject;
            }
            catch
            {
                return;
            }

            if (parsedObject is null)
            {
                return;
            }

            if (string.Equals(parsedObject["type"]?.GetValue<string>(), "control_request", StringComparison.Ordinal) &&
                parsedObject["request"] is JsonObject request &&
                string.Equals(request["subtype"]?.GetValue<string>(), "can_use_tool", StringComparison.Ordinal))
            {
                _dependencies.OnPermissionRequest?.Invoke(_options.SessionId, parsedObject, AccessToken);
                return;
            }

            if (!_firstUserMessageSeen &&
                _options.OnFirstUserMessage is not null &&
                string.Equals(parsedObject["type"]?.GetValue<string>(), "user", StringComparison.Ordinal))
            {
                var text = SessionRunnerUtilities.ExtractUserMessageText(parsedObject);
                if (!string.IsNullOrEmpty(text))
                {
                    _firstUserMessageSeen = true;
                    _options.OnFirstUserMessage(text);
                }
            }
        }

        private void HandleErrorLine(string line)
        {
            if (_dependencies.Verbose)
            {
                Console.Error.WriteLine(line);
            }

            if (_lastStderr.Count >= SessionRunnerUtilities.MaxStderrLines)
            {
                _lastStderr.RemoveAt(0);
            }

            _lastStderr.Add(line);
        }

        private void HandleExit(int? exitCode)
        {
            _transcriptWriter?.Dispose();
            if (_terminated)
            {
                _dependencies.OnDebug?.Invoke(
                    $"[bridge:session] sessionId={_options.SessionId} interrupted pid={_process.ProcessId}");
                _doneSource.TrySetResult(SessionDoneStatus.Interrupted);
                return;
            }

            if (exitCode == 0)
            {
                _dependencies.OnDebug?.Invoke(
                    $"[bridge:session] sessionId={_options.SessionId} completed exit_code=0 pid={_process.ProcessId}");
                _doneSource.TrySetResult(SessionDoneStatus.Completed);
                return;
            }

            _dependencies.OnDebug?.Invoke(
                $"[bridge:session] sessionId={_options.SessionId} failed exit_code={exitCode} pid={_process.ProcessId}");
            _doneSource.TrySetResult(SessionDoneStatus.Failed);
        }

        private static string TruncateForDebug(string value)
        {
            ArgumentNullException.ThrowIfNull(value);

            const int maxLength = 300;
            var normalized = value.ReplaceLineEndings("\\n");
            return normalized.Length <= maxLength
                ? normalized
                : normalized[..maxLength];
        }
    }
}

internal sealed class SessionChildProcessFactory : ISessionChildProcessFactory
{
    public ISessionChildProcess Start(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process '{startInfo.FileName}'.");
        }

        return new SessionChildProcess(process);
    }
}

internal sealed class SessionChildProcess : ISessionChildProcess
{
    private readonly Process _process;

    public SessionChildProcess(Process process)
    {
        _process = process ?? throw new ArgumentNullException(nameof(process));
        _process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                OutputLineReceived?.Invoke(eventArgs.Data);
            }
        };
        _process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is not null)
            {
                ErrorLineReceived?.Invoke(eventArgs.Data);
            }
        };
        _process.Exited += (_, _) => Exited?.Invoke(_process.ExitCode);
    }

    public int? ProcessId => _process.HasExited ? _process.Id : _process.Id;

    public event Action<string>? OutputLineReceived;

    public event Action<string>? ErrorLineReceived;

    public event Action<int?>? Exited;

    public void BeginReading()
    {
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public void WriteToStandardInput(string data)
    {
        if (_process.StandardInput.BaseStream.CanWrite)
        {
            _process.StandardInput.Write(data);
            _process.StandardInput.Flush();
        }
    }

    public void Terminate()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
        }
    }

    public void ForceTerminate()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
        }
    }
}
