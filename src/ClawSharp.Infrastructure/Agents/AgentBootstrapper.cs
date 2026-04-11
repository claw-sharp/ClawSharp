using System.Diagnostics;
using System.Text;
using ClawSharp.Core;
using YamlDotNet.Serialization;

namespace ClawSharp.Infrastructure;

public sealed class AgentBootstrapper
{
    private static readonly TimeSpan GitCommandTimeout = TimeSpan.FromSeconds(5);
    private const long SlowAgentFileThresholdMs = 250;
    private readonly string _managedFilePath;
    private readonly string _userConfigHomeDir;
    private readonly IDeserializer _yamlDeserializer;
    private readonly Func<string, CancellationToken, Task<string?>> _canonicalGitRootResolver;

    public AgentBootstrapper(
        string? managedFilePath = null,
        string? userConfigHomeDir = null,
        Func<string, CancellationToken, Task<string?>>? canonicalGitRootResolver = null)
    {
        _managedFilePath = managedFilePath ?? ClaudeConfigPaths.GetManagedFilePath();
        _userConfigHomeDir = userConfigHomeDir ?? SessionStoragePaths.GetClaudeConfigHomeDir();
        _yamlDeserializer = new DeserializerBuilder().Build();
        _canonicalGitRootResolver = canonicalGitRootResolver ?? TryGetCanonicalGitRootAsync;
    }

    public async Task<AgentDefinitionsCatalog> LoadAsync(
        string workspaceRoot,
        StartupEnvironment startupEnvironment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        workspaceRoot = Path.GetFullPath(workspaceRoot);
        var stopwatch = Stopwatch.StartNew();
        LogInfo("load", $"start workspace={workspaceRoot} bareMode={startupEnvironment.BareMode}");

        try
        {
            var builtInAgents = BuiltInAgentDefinitions.GetBuiltInAgents();
            if (startupEnvironment.BareMode)
            {
                stopwatch.Stop();
                LogInfo("load", $"complete workspace={workspaceRoot} bareMode=true activeCount={builtInAgents.Count} totalCount={builtInAgents.Count} failureCount=0 elapsedMs={stopwatch.ElapsedMilliseconds}");
                return new AgentDefinitionsCatalog(builtInAgents, builtInAgents);
            }

            var failures = new List<AgentLoadFailure>();
            var customAgents = new List<AgentDefinition>();

            AddAgentsFromDirectory(Path.Combine(_managedFilePath, ".clawsharp", "agents"), "policySettings", customAgents, failures, cancellationToken);
            AddAgentsFromDirectory(Path.Combine(_userConfigHomeDir, "agents"), "userSettings", customAgents, failures, cancellationToken);

            foreach (var projectAgentsDirectory in await GetProjectAgentDirectoriesUpToHomeAsync(workspaceRoot, cancellationToken))
            {
                AddAgentsFromDirectory(projectAgentsDirectory, "projectSettings", customAgents, failures, cancellationToken);
            }

            var allAgents = builtInAgents.Concat(customAgents).ToArray();
            var activeAgents = GetActiveAgentsFromList(allAgents);
            stopwatch.Stop();
            LogInfo("load", $"complete workspace={workspaceRoot} bareMode=false activeCount={activeAgents.Count} totalCount={allAgents.Length} failureCount={failures.Count} elapsedMs={stopwatch.ElapsedMilliseconds}");
            return new AgentDefinitionsCatalog(
                activeAgents,
                allAgents,
                failures.Count == 0 ? null : failures);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            LogWarn("load", $"failed workspace={workspaceRoot} elapsedMs={stopwatch.ElapsedMilliseconds} error={ex.GetType().Name}: {ex.Message}");
            throw;
        }
    }

    private void AddAgentsFromDirectory(
        string basePath,
        string source,
        List<AgentDefinition> discoveredAgents,
        List<AgentLoadFailure> failures,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        LogInfo("directory-scan", $"start source={source} path={Path.GetFullPath(basePath)}");
        if (!Directory.Exists(basePath))
        {
            stopwatch.Stop();
            LogInfo("directory-scan", $"skip source={source} path={Path.GetFullPath(basePath)} reason=not-found elapsedMs={stopwatch.ElapsedMilliseconds}");
            return;
        }

        var startingAgentCount = discoveredAgents.Count;
        var startingFailureCount = failures.Count;
        var filePaths = Directory.GetFiles(basePath, "*.md", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, GetPathComparer())
            .ToArray();

        foreach (var filePath in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileStopwatch = Stopwatch.StartNew();
            var agent = TryParseAgent(filePath, basePath, source, out var error);
            fileStopwatch.Stop();

            if (agent is not null)
            {
                discoveredAgents.Add(agent);
            }
            else if (!string.IsNullOrWhiteSpace(error))
            {
                failures.Add(new AgentLoadFailure(filePath, error));
            }

            if (fileStopwatch.ElapsedMilliseconds >= SlowAgentFileThresholdMs)
            {
                LogWarn("agent-file-slow", $"source={source} path={filePath} elapsedMs={fileStopwatch.ElapsedMilliseconds}");
            }
        }

        stopwatch.Stop();
        LogInfo(
            "directory-scan",
            $"complete source={source} path={Path.GetFullPath(basePath)} fileCount={filePaths.Length} discoveredCount={discoveredAgents.Count - startingAgentCount} failureCount={failures.Count - startingFailureCount} elapsedMs={stopwatch.ElapsedMilliseconds}");
    }

    private AgentDefinition? TryParseAgent(
        string filePath,
        string baseDirectory,
        string source,
        out string? error)
    {
        error = null;

        var text = File.ReadAllText(filePath, Encoding.UTF8);
        var (frontmatter, content) = SplitFrontmatter(text);
        if (frontmatter is null || frontmatter.Count == 0)
        {
            return null;
        }

        if (!TryGetString(frontmatter, "name", out var agentType))
        {
            return null;
        }

        if (!TryGetString(frontmatter, "description", out var whenToUse))
        {
            error = "Missing required \"description\" field in frontmatter";
            return null;
        }

        return new AgentDefinition(
            AgentType: agentType,
            WhenToUse: whenToUse.Replace("\\n", "\n", StringComparison.Ordinal),
            Source: source,
            BaseDirectory: Path.GetFullPath(baseDirectory),
            SystemPrompt: content.Trim(),
            Tools: ParseAgentToolList(TryGetValue(frontmatter, "tools")),
            DisallowedTools: ParseAgentToolList(TryGetValue(frontmatter, "disallowedTools")),
            Skills: ParseStringList(TryGetValue(frontmatter, "skills")),
            Color: TryGetOptionalString(frontmatter, "color"),
            Model: TryGetOptionalString(frontmatter, "model"),
            PermissionMode: TryParsePermissionMode(TryGetOptionalString(frontmatter, "permissionMode")),
            MaxTurns: TryParsePositiveInt(TryGetValue(frontmatter, "maxTurns")),
            Filename: Path.GetFileNameWithoutExtension(filePath),
            Background: TryParseBoolean(TryGetValue(frontmatter, "background")),
            InitialPrompt: TryGetOptionalString(frontmatter, "initialPrompt"),
            Memory: TryGetOptionalString(frontmatter, "memory"),
            Isolation: TryGetOptionalString(frontmatter, "isolation"),
            OmitClaudeMd: TryParseBoolean(TryGetValue(frontmatter, "omitClaudeMd")) ?? false);
    }

    private static IReadOnlyList<AgentDefinition> GetActiveAgentsFromList(
        IReadOnlyList<AgentDefinition> allAgents)
    {
        var agentMap = new Dictionary<string, AgentDefinition>(StringComparer.Ordinal);
        foreach (var group in new[]
                 {
                     allAgents.Where(static agent => agent.Source == "built-in"),
                     allAgents.Where(static agent => agent.Source == "userSettings"),
                     allAgents.Where(static agent => agent.Source == "projectSettings"),
                     allAgents.Where(static agent => agent.Source == "policySettings")
                 })
        {
            foreach (var agent in group)
            {
                agentMap[agent.AgentType] = agent;
            }
        }

        return agentMap.Values.ToArray();
    }

    private (Dictionary<string, object?>? Frontmatter, string Content) SplitFrontmatter(string text)
    {
        if (!text.StartsWith("---", StringComparison.Ordinal))
        {
            return (null, text);
        }

        using var reader = new StringReader(text);
        if (!string.Equals(reader.ReadLine(), "---", StringComparison.Ordinal))
        {
            return (null, text);
        }

        var frontmatterBuilder = new StringBuilder();
        string? line;
        var foundClosing = false;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.Equals(line, "---", StringComparison.Ordinal))
            {
                foundClosing = true;
                break;
            }

            frontmatterBuilder.AppendLine(line);
        }

        if (!foundClosing)
        {
            return (null, text);
        }

        var content = reader.ReadToEnd();
        var parsed = _yamlDeserializer.Deserialize<Dictionary<object, object?>>(frontmatterBuilder.ToString()) ?? [];
        var normalized = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in parsed)
        {
            if (pair.Key is null)
            {
                continue;
            }

            normalized[pair.Key.ToString() ?? string.Empty] = pair.Value;
        }

        return (normalized, content);
    }

    private async Task<IReadOnlyList<string>> GetProjectAgentDirectoriesUpToHomeAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        LogInfo("project-directories", $"start workspace={workspaceRoot}");
        var homeDirectory = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var gitRoot = await ResolveCanonicalGitRootAsync(workspaceRoot, cancellationToken);
        var current = Path.GetFullPath(workspaceRoot);
        var directories = new List<string>();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (PathsEqual(current, homeDirectory))
            {
                break;
            }

            var agentsDirectory = Path.Combine(current, ".clawsharp", "agents");
            if (Directory.Exists(agentsDirectory))
            {
                directories.Add(agentsDirectory);
            }

            if (gitRoot is not null && PathsEqual(current, gitRoot))
            {
                break;
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (string.IsNullOrWhiteSpace(parent) || PathsEqual(parent, current))
            {
                break;
            }

            current = parent;
        }

        stopwatch.Stop();
        LogInfo(
            "project-directories",
            $"complete workspace={workspaceRoot} directoryCount={directories.Count} gitRoot={gitRoot ?? "<none>"} elapsedMs={stopwatch.ElapsedMilliseconds}");
        return directories;
    }

    private async Task<string?> ResolveCanonicalGitRootAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        LogInfo("git-root", $"start workspace={workspaceRoot}");

        try
        {
            var gitRoot = await _canonicalGitRootResolver(workspaceRoot, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            LogInfo("git-root", $"complete workspace={workspaceRoot} root={gitRoot ?? "<none>"} elapsedMs={stopwatch.ElapsedMilliseconds}");
            return gitRoot;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            LogWarn("git-root", $"canceled workspace={workspaceRoot} elapsedMs={stopwatch.ElapsedMilliseconds}");
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            LogWarn("git-root", $"failed workspace={workspaceRoot} elapsedMs={stopwatch.ElapsedMilliseconds} error={ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static IReadOnlyList<string>? ParseAgentToolList(object? value)
    {
        var parsed = ParseStringList(value);
        if (value is null)
        {
            return null;
        }

        if (parsed is null || parsed.Count == 0)
        {
            return [];
        }

        return parsed.Contains("*", StringComparer.Ordinal) ? null : parsed;
    }

    private static IReadOnlyList<string>? ParseStringList(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string stringValue)
        {
            return string.IsNullOrWhiteSpace(stringValue) ? [] : [stringValue.Trim()];
        }

        if (value is IEnumerable<object?> sequence)
        {
            return sequence
                .Select(static item => item?.ToString()?.Trim())
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToArray();
        }

        return [];
    }

    private static PermissionMode? TryParsePermissionMode(string? value)
    {
        return value?.Trim() switch
        {
            "default" => PermissionMode.Default,
            "acceptEdits" => PermissionMode.AcceptEdits,
            "bypassPermissions" => PermissionMode.BypassPermissions,
            "dontAsk" => PermissionMode.DontAsk,
            "plan" => PermissionMode.Plan,
            "auto" => PermissionMode.Auto,
            "bubble" => PermissionMode.Bubble,
            _ => null
        };
    }

    private static int? TryParsePositiveInt(object? value)
    {
        if (value is not null && int.TryParse(value.ToString(), out var parsed) && parsed > 0)
        {
            return parsed;
        }

        return null;
    }

    private static bool? TryParseBoolean(object? value)
    {
        if (value is bool boolValue)
        {
            return boolValue;
        }

        if (value is not null && bool.TryParse(value.ToString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool TryGetString(Dictionary<string, object?> values, string key, out string value)
    {
        value = string.Empty;
        if (!values.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        value = raw.ToString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string? TryGetOptionalString(Dictionary<string, object?> values, string key)
    {
        return values.TryGetValue(key, out var raw) && raw is not null
            ? raw.ToString()
            : null;
    }

    private static object? TryGetValue(Dictionary<string, object?> values, string key)
    {
        return values.TryGetValue(key, out var raw) ? raw : null;
    }

    private static async Task<string?> TryGetCanonicalGitRootAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = "rev-parse --show-toplevel",
            WorkingDirectory = workspaceRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return null;
            }
        }
        catch
        {
            return null;
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(GitCommandTimeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKillProcess(process);
            await AwaitExitQuietlyAsync(process).ConfigureAwait(false);
            _ = await standardOutputTask.ConfigureAwait(false);
            var timeoutError = await standardErrorTask.ConfigureAwait(false);
            LogWarn("git-root", $"timeout workspace={workspaceRoot} timeoutMs={GitCommandTimeout.TotalMilliseconds} stderr={TrimForLog(timeoutError)}");
            return null;
        }

        var output = (await standardOutputTask.ConfigureAwait(false)).Trim();
        var error = (await standardErrorTask.ConfigureAwait(false)).Trim();
        if (process.ExitCode != 0)
        {
            LogWarn("git-root", $"non-zero-exit workspace={workspaceRoot} exitCode={process.ExitCode} stderr={TrimForLog(error)}");
            return null;
        }

        return string.IsNullOrWhiteSpace(output) ? null : Path.GetFullPath(output);
    }

    private static async Task AwaitExitQuietlyAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private static string TrimForLog(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "<empty>";
        }

        var singleLine = value.ReplaceLineEndings(" ").Trim();
        return singleLine.Length > 240 ? singleLine[..240] : singleLine;
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), GetPathComparison());
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    private static void LogInfo(string category, string message)
    {
        ClawSharpTelemetry.LogDebug($"[AgentBootstrapper:{category}] {message}", DebugLogLevel.Info);
    }

    private static void LogWarn(string category, string message)
    {
        ClawSharpTelemetry.LogDebug($"[AgentBootstrapper:{category}] {message}", DebugLogLevel.Warn);
    }
}
