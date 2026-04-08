using System.Text;
using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Tasks;

namespace ClawSharp.Infrastructure;

public sealed class HookExecutor
{
    private readonly HttpClient _httpClient;
    private readonly PluginOptionService _pluginOptionService;

    public HookExecutor(HttpClient? httpClient = null, PluginOptionService? pluginOptionService = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _pluginOptionService = pluginOptionService ?? new PluginOptionService(new UnsupportedMcpSecureStorage());
    }

    public async Task<HookExecutionBatchResult> ExecuteAsync(
        IReadOnlyList<HookDefinition> hooks,
        HookExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var traces = new List<HookExecutionTrace>();
        var blockingFailure = false;

        await foreach (var update in StreamAsync(hooks, request, cancellationToken))
        {
            if (update.Trace is not null)
            {
                traces.Add(update.Trace);
                if (!update.Trace.Succeeded)
                {
                    blockingFailure = true;
                }
            }
        }

        return new HookExecutionBatchResult(traces, blockingFailure);
    }

    public async IAsyncEnumerable<HookExecutionUpdate> StreamAsync(
        IReadOnlyList<HookDefinition> hooks,
        HookExecutionRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var hook in hooks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matched = string.IsNullOrWhiteSpace(hook.Matcher) ||
                          hook.Matcher == "*" ||
                          string.Equals(hook.Matcher, request.MatcherValue, StringComparison.Ordinal);
            if (!matched)
            {
                yield return new HookExecutionUpdate(
                    Trace: new HookExecutionTrace(hook, false, true, null, string.Empty, string.Empty, null));
                continue;
            }

            var displayHookName = string.IsNullOrWhiteSpace(request.HookName)
                ? request.Event.ToString()
                : request.HookName;
            if (!string.IsNullOrWhiteSpace(request.ToolUseId))
            {
                yield return new HookExecutionUpdate(
                    Message: ChatMessageFactory.CreateProgress(
                        request.ToolUseId,
                        request.ToolUseId,
                        CreateHookProgressData(request.Event, displayHookName!, hook.Command)));
            }

            var trace = await ExecuteSingleAsync(hook, request, cancellationToken);
            yield return new HookExecutionUpdate(Trace: trace);

            var updates = CreateExecutionOutcomeUpdates(trace, request, displayHookName!);
            foreach (var update in updates)
            {
                yield return update;
            }
        }
    }

    private async Task<HookExecutionTrace> ExecuteSingleAsync(
        HookDefinition hook,
        HookExecutionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return hook.Command.Type switch
            {
                HookKind.Command => await ExecuteCommandHookAsync(hook, request, _pluginOptionService, cancellationToken),
                HookKind.Http => await ExecuteHttpHookAsync(hook, request, cancellationToken),
                HookKind.Prompt or HookKind.Agent => new HookExecutionTrace(
                    hook,
                    true,
                    false,
                    null,
                    string.Empty,
                    string.Empty,
                    $"Hook type '{hook.Command.Type}' is not executable until the model-backed hook path is ported."),
                _ => new HookExecutionTrace(hook, true, false, null, string.Empty, string.Empty, "Unsupported hook type.")
            };
        }
        catch (Exception ex)
        {
            return new HookExecutionTrace(hook, true, false, null, string.Empty, string.Empty, ex.Message);
        }
    }

    private static async Task<HookExecutionTrace> ExecuteCommandHookAsync(
        HookDefinition hook,
        HookExecutionRequest request,
        PluginOptionService pluginOptionService,
        CancellationToken cancellationToken)
    {
        var shell = hook.Command.Shell ?? HookShell.Bash;
        var shellPath = await ResolveShellPathAsync(shell, cancellationToken);
        if (string.IsNullOrWhiteSpace(shellPath) || string.IsNullOrWhiteSpace(hook.Command.Command))
        {
            return new HookExecutionTrace(hook, true, false, null, string.Empty, string.Empty, "Hook shell is not available.");
        }

        var isPowerShell = shell == HookShell.PowerShell;
        var environmentOverrides = new Dictionary<string, string>(
            request.EnvironmentVariables ?? new Dictionary<string, string>(StringComparer.Ordinal),
            StringComparer.Ordinal);
        var command = hook.Command.Command;
        try
        {
            command = ApplyPluginVariables(hook, request, command, pluginOptionService, isPowerShell, environmentOverrides);
        }
        catch (Exception ex)
        {
            return new HookExecutionTrace(hook, true, false, null, string.Empty, string.Empty, ex.Message);
        }

        var outputPath = TaskOutputStoragePaths.GetTaskOutputPath(
            request.WorkingDirectory,
            "hooks",
            $"hook-{Guid.NewGuid():N}");
        await new DiskTaskOutputStore().InitTaskOutputAsync(outputPath, cancellationToken);
        var taskOutput = new TaskOutput($"h{Guid.NewGuid():N}"[..9], outputPath);

        IReadOnlyList<string> arguments;
        IReadOnlyDictionary<string, string>? shellEnvironmentOverrides = environmentOverrides;
        if (isPowerShell)
        {
            var provider = new PowerShellShellProvider(shellPath);
            var execCommand = provider.BuildExecCommand(command, Guid.NewGuid().ToString("N"));
            arguments = provider.GetSpawnArguments(execCommand.CommandString);
            shellEnvironmentOverrides = MergeEnvironment(environmentOverrides, provider.GetEnvironmentOverrides());
        }
        else
        {
            arguments = ["-lc", command];
        }

        var runner = new LocalShellProcessRunner();
        var process = await runner.StartAsync(
            new LocalShellProcessStartInfo(
                shellPath,
                arguments,
                request.WorkingDirectory,
                taskOutput,
                Math.Max(1, hook.Command.TimeoutSeconds ?? 60) * 1000,
                shellEnvironmentOverrides),
            cancellationToken);

        var result = await process.Result.WaitAsync(cancellationToken);
        return new HookExecutionTrace(hook, true, result.Code == 0, result.Code, result.Stdout, result.Stderr, result.Code == 0 ? null : result.Stderr);
    }

    private async Task<HookExecutionTrace> ExecuteHttpHookAsync(
        HookDefinition hook,
        HookExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(hook.Command.Url))
        {
            return new HookExecutionTrace(hook, true, false, null, string.Empty, string.Empty, "HTTP hook URL was empty.");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, hook.Command.Url)
        {
            Content = new StringContent(request.InputJson, Encoding.UTF8, "application/json")
        };

        if (hook.Command.Headers is not null)
        {
            foreach (var header in hook.Command.Headers)
            {
                if (!httpRequest.Headers.TryAddWithoutValidation(header.Key, header.Value))
                {
                    httpRequest.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
        }

        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(1, hook.Command.TimeoutSeconds ?? 60)));
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        var response = await _httpClient.SendAsync(httpRequest, linkedSource.Token);
        var content = await response.Content.ReadAsStringAsync(linkedSource.Token);
        return new HookExecutionTrace(hook, true, response.IsSuccessStatusCode, (int)response.StatusCode, content, string.Empty, response.IsSuccessStatusCode ? null : response.ReasonPhrase);
    }

    private static async Task<string?> ResolveShellPathAsync(HookShell? shell, CancellationToken cancellationToken)
    {
        if ((shell ?? HookShell.Bash) == HookShell.PowerShell)
        {
            return await PowerShellDetection.GetCachedPowerShellPathAsync();
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await BashShellDetection.FindSuitableShellAsync();
    }

    private static IReadOnlyDictionary<string, string> MergeEnvironment(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);
        if (left is not null)
        {
            foreach (var pair in left)
            {
                merged[pair.Key] = pair.Value;
            }
        }

        if (right is not null)
        {
            foreach (var pair in right)
            {
                merged[pair.Key] = pair.Value;
            }
        }

        return merged;
    }

    private static string ApplyPluginVariables(
        HookDefinition hook,
        HookExecutionRequest request,
        string command,
        PluginOptionService pluginOptionService,
        bool isPowerShell,
        IDictionary<string, string> environmentOverrides)
    {
        var updated = command;
        if (!string.IsNullOrWhiteSpace(hook.PluginRoot))
        {
            if (!Directory.Exists(hook.PluginRoot))
            {
                throw new InvalidOperationException($"Plugin directory does not exist: {hook.PluginRoot}");
            }

            var rootPath = ToHookPath(hook.PluginRoot, isPowerShell);
            updated = updated.Replace("${CLAUDE_PLUGIN_ROOT}", rootPath, StringComparison.Ordinal);
            environmentOverrides["CLAUDE_PLUGIN_ROOT"] = rootPath;

            if (!string.IsNullOrWhiteSpace(hook.PluginId))
            {
                var dataDirectory = pluginOptionService.GetPluginDataDirectory(hook.PluginId);
                var dataPath = ToHookPath(dataDirectory, isPowerShell);
                updated = updated.Replace("${CLAUDE_PLUGIN_DATA}", dataPath, StringComparison.Ordinal);
                environmentOverrides["CLAUDE_PLUGIN_DATA"] = dataPath;

                var options = pluginOptionService.LoadPluginOptions(hook.PluginId, request.Settings ?? new ClawSharpSettings());
                updated = pluginOptionService.SubstituteUserConfigVariables(updated, options);
                foreach (var option in options)
                {
                    if (option.Value is null)
                    {
                        continue;
                    }

                    environmentOverrides[$"CLAUDE_PLUGIN_OPTION_{SanitizeOptionEnvironmentKey(option.Key)}"] =
                        Convert.ToString(option.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(hook.SkillRoot))
        {
            environmentOverrides["CLAUDE_PLUGIN_ROOT"] = ToHookPath(hook.SkillRoot, isPowerShell);
        }

        return updated;
    }

    private static string ToHookPath(string path, bool isPowerShell)
    {
        if (!OperatingSystem.IsWindows() || isPowerShell)
        {
            return path;
        }

        return WindowsPathConversion.WindowsPathToPosixPath(path);
    }

    private static string SanitizeOptionEnvironmentKey(string key)
    {
        var buffer = new char[key.Length];
        for (var index = 0; index < key.Length; index++)
        {
            var character = key[index];
            buffer[index] =
                (character >= 'a' && character <= 'z') ||
                (character >= 'A' && character <= 'Z') ||
                (character >= '0' && character <= '9') ||
                character == '_'
                    ? char.ToUpperInvariant(character)
                    : '_';
        }

        return new string(buffer);
    }

    private static IReadOnlyList<HookExecutionUpdate> CreateExecutionOutcomeUpdates(
        HookExecutionTrace trace,
        HookExecutionRequest request,
        string hookName)
    {
        if (string.IsNullOrWhiteSpace(request.ToolUseId))
        {
            return [];
        }

        var commandDisplay = GetHookDisplayText(trace.Hook.Command);
        var syncJsonUpdates = TryCreateSyncJsonExecutionOutcomeUpdates(trace, request, hookName, commandDisplay);
        if (syncJsonUpdates is not null)
        {
            return syncJsonUpdates;
        }

        if (trace.Succeeded)
        {
            return
            [
                new HookExecutionUpdate(
                    Message: ChatMessageFactory.CreateHookSuccessAttachmentMessage(
                        content: trace.Stdout.Trim(),
                        hookName: hookName,
                        toolUseId: request.ToolUseId,
                        hookEvent: request.Event,
                        stdout: trace.Stdout,
                        stderr: trace.Stderr,
                        exitCode: trace.ExitCode,
                        command: commandDisplay))
            ];
        }

        if (trace.ExitCode == 2)
        {
            var blocking = new HookBlockingError(
                $"[{commandDisplay}]: {(string.IsNullOrWhiteSpace(trace.Stderr) ? "No stderr output" : trace.Stderr)}",
                commandDisplay);

            return
            [
                new HookExecutionUpdate(
                    Message: ChatMessageFactory.CreateHookBlockingErrorAttachmentMessage(
                        blocking,
                        hookName,
                        request.ToolUseId!,
                        request.Event),
                    BlockingError: blocking)
            ];
        }

        if (trace.Error is not null && trace.ExitCode is null)
        {
            return
            [
                new HookExecutionUpdate(
                    Message: ChatMessageFactory.CreateHookErrorDuringExecutionAttachmentMessage(
                        content: trace.Error,
                        hookName: hookName,
                        toolUseId: request.ToolUseId,
                        hookEvent: request.Event,
                        command: commandDisplay))
            ];
        }

        return
        [
            new HookExecutionUpdate(
                Message: ChatMessageFactory.CreateHookNonBlockingErrorAttachmentMessage(
                    hookName: hookName,
                    stderr: string.IsNullOrWhiteSpace(trace.Stderr)
                        ? (trace.Error ?? "Hook execution failed.")
                        : trace.Stderr,
                    stdout: trace.Stdout,
                    exitCode: trace.ExitCode ?? 1,
                    toolUseId: request.ToolUseId,
                    hookEvent: request.Event,
                    command: commandDisplay))
        ];
    }

    private static IReadOnlyList<HookExecutionUpdate>? TryCreateSyncJsonExecutionOutcomeUpdates(
        HookExecutionTrace trace,
        HookExecutionRequest request,
        string hookName,
        string commandDisplay)
    {
        HookSyncJsonParseResult parseResult;
        if (trace.Hook.Command.Type == HookKind.Http)
        {
            if (!trace.Succeeded)
            {
                return null;
            }

            parseResult = HookSyncJsonOutputProcessor.ParseHttpOutput(trace.Stdout);
        }
        else if (trace.Hook.Command.Type == HookKind.Command)
        {
            parseResult = HookSyncJsonOutputProcessor.ParseCommandOutput(trace.Stdout);
        }
        else
        {
            return null;
        }

        if (parseResult.ValidationError is not null)
        {
            return
            [
                new HookExecutionUpdate(
                    Message: ChatMessageFactory.CreateHookNonBlockingErrorAttachmentMessage(
                        hookName: hookName,
                        stderr: trace.Hook.Command.Type == HookKind.Http
                            ? parseResult.ValidationError
                            : $"JSON validation failed: {parseResult.ValidationError}",
                        stdout: trace.Stdout,
                        exitCode: trace.Hook.Command.Type == HookKind.Http
                            ? (trace.ExitCode ?? 0)
                            : 1,
                        toolUseId: request.ToolUseId!,
                        hookEvent: request.Event,
                        command: commandDisplay))
            ];
        }

        if (parseResult.Parsed is null)
        {
            return null;
        }

        try
        {
            return HookSyncJsonOutputProcessor.ProcessSyncOutput(
                parseResult.Parsed,
                commandDisplay,
                hookName,
                request.ToolUseId!,
                request.Event,
                trace.Stdout,
                trace.Stderr,
                trace.ExitCode);
        }
        catch (Exception ex)
        {
            return
            [
                new HookExecutionUpdate(
                    Message: ChatMessageFactory.CreateHookNonBlockingErrorAttachmentMessage(
                        hookName: hookName,
                        stderr: $"Failed to run: {ex.Message}",
                        stdout: trace.Stdout,
                        exitCode: 1,
                        toolUseId: request.ToolUseId!,
                        hookEvent: request.Event,
                        command: commandDisplay))
            ];
        }
    }

    private static JsonObject CreateHookProgressData(
        HookEvent hookEvent,
        string hookName,
        HookCommandDefinition hook)
    {
        var data = new JsonObject
        {
            ["type"] = "hook_progress",
            ["hookEvent"] = hookEvent.ToString(),
            ["hookName"] = hookName,
            ["command"] = GetHookDisplayText(hook)
        };

        if (!string.IsNullOrWhiteSpace(hook.Prompt))
        {
            data["promptText"] = hook.Prompt;
        }

        if (!string.IsNullOrWhiteSpace(hook.StatusMessage))
        {
            data["statusMessage"] = hook.StatusMessage;
        }

        return data;
    }

    private static string GetHookDisplayText(HookCommandDefinition hook)
    {
        if (!string.IsNullOrWhiteSpace(hook.StatusMessage))
        {
            return hook.StatusMessage;
        }

        return hook.Type switch
        {
            HookKind.Command => hook.Command ?? "command",
            HookKind.Prompt => hook.Prompt ?? "prompt",
            HookKind.Agent => hook.Prompt ?? "agent",
            HookKind.Http => hook.Url ?? "http",
            _ => hook.Type.ToString()
        };
    }
}
