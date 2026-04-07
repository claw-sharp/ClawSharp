using System.Net;
using System.Net.Http;
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Query;
using ClawSharp.Tasks;

namespace ClawSharp.UnitTests;

public sealed class HookRegistryTests
{
    [Fact]
    public void GetHooksForEvent_Prioritizes_Settings_Before_Plugins_And_Exact_Matchers_Before_Wildcards()
    {
        var hooks = new[]
        {
            new HookDefinition(
                HookEvent.PreToolUse,
                "plugin",
                "*",
                new HookCommandDefinition(HookKind.Command, Command: "plugin")),
            new HookDefinition(
                HookEvent.PreToolUse,
                "settings",
                "*",
                new HookCommandDefinition(HookKind.Command, Command: "settings-wildcard")),
            new HookDefinition(
                HookEvent.PreToolUse,
                "settings",
                "Write",
                new HookCommandDefinition(HookKind.Command, Command: "settings-exact"))
        };

        var ordered = new HookRegistry().GetHooksForEvent(hooks, HookEvent.PreToolUse, "Write");

        Assert.Equal(
            ["settings-exact", "settings-wildcard", "plugin"],
            ordered.Select(static hook => hook.Command.Command ?? string.Empty).ToArray());
    }

    [Fact]
    public async Task HookExecutor_Executes_Http_Hooks_And_Captures_Response()
    {
        using var client = new HttpClient(new StaticHttpMessageHandler(HttpStatusCode.OK, "accepted"));
        var executor = new HookExecutor(client);
        var hook = new HookDefinition(
            HookEvent.PostToolUse,
            "settings",
            null,
            new HookCommandDefinition(HookKind.Http, Url: "https://example.test/hook", TimeoutSeconds: 5));

        var result = await executor.ExecuteAsync(
            [hook],
            new HookExecutionRequest(HookEvent.PostToolUse, Environment.CurrentDirectory, """{"ok":true}"""));

        var trace = Assert.Single(result.Traces);
        Assert.True(trace.Succeeded);
        Assert.Equal(200, trace.ExitCode);
        Assert.Equal("accepted", trace.Stdout);
    }

    [Fact]
    public async Task HookExecutor_Substitutes_Plugin_Variables_And_Exports_Plugin_Option_Environment()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "clawsharp-hook-plugin-vars", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var shell = OperatingSystem.IsWindows() ? HookShell.PowerShell : HookShell.Bash;
        var shellPath = shell == HookShell.PowerShell
            ? await PowerShellDetection.GetCachedPowerShellPathAsync()
            : await BashShellDetection.FindSuitableShellAsync();
        if (string.IsNullOrWhiteSpace(shellPath))
        {
            return;
        }

        try
        {
            var pluginRoot = Path.Combine(tempRoot, "plugin-root");
            Directory.CreateDirectory(pluginRoot);
            var settings = new ClawSharpSettings
            {
                PluginConfigs = new Dictionary<string, PluginConfigSettings>(StringComparer.Ordinal)
                {
                    ["reviewer@anthropic-tools"] = new PluginConfigSettings
                    {
                        Options = new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["style"] = "strict"
                        }
                    }
                }
            };
            var secureStorage = new InMemorySecureStorage(
                new McpSecureStorageData(
                    PluginSecrets: new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
                    {
                        ["reviewer@anthropic-tools"] = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["apiKey"] = "top-secret"
                        }
                    }));
            var optionService = new PluginOptionService(secureStorage, Path.Combine(tempRoot, "plugins"));
            var executor = new HookExecutor(pluginOptionService: optionService);
            var command = shell == HookShell.PowerShell
                ? "Write-Output '${CLAUDE_PLUGIN_ROOT}'; Write-Output '${CLAUDE_PLUGIN_DATA}'; Write-Output '${user_config.apiKey}'; Write-Output $env:CLAUDE_PLUGIN_OPTION_APIKEY; Write-Output $env:CLAUDE_PLUGIN_OPTION_STYLE"
                : "printf '%s\\n' '${CLAUDE_PLUGIN_ROOT}' '${CLAUDE_PLUGIN_DATA}' '${user_config.apiKey}' \"$CLAUDE_PLUGIN_OPTION_APIKEY\" \"$CLAUDE_PLUGIN_OPTION_STYLE\"";
            var hook = new HookDefinition(
                HookEvent.PreToolUse,
                "plugin",
                null,
                new HookCommandDefinition(HookKind.Command, Command: command, Shell: shell, TimeoutSeconds: 5),
                PluginId: "reviewer@anthropic-tools",
                PluginRoot: pluginRoot);

            var result = await executor.ExecuteAsync(
                [hook],
                new HookExecutionRequest(
                    HookEvent.PreToolUse,
                    tempRoot,
                    """{"ok":true}""",
                    Settings: settings));

            var trace = Assert.Single(result.Traces);
            Assert.True(trace.Succeeded);
            Assert.Contains("top-secret", trace.Stdout, StringComparison.Ordinal);
            Assert.Contains("strict", trace.Stdout, StringComparison.Ordinal);
            Assert.Contains(
                optionService.GetPluginDataDirectory("reviewer@anthropic-tools").Replace('\\', '/'),
                trace.Stdout.Replace('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task HookExecutor_On_Windows_Bash_Uses_Posix_Plugin_Paths_And_Working_Directory()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var shellPath = await BashShellDetection.FindSuitableShellAsync();
        if (string.IsNullOrWhiteSpace(shellPath))
        {
            return;
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "clawsharp-hook-windows-bash", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var pluginRoot = Path.Combine(tempRoot, "plugin-root");
            Directory.CreateDirectory(pluginRoot);
            var settings = new ClawSharpSettings
            {
                PluginConfigs = new Dictionary<string, PluginConfigSettings>(StringComparer.Ordinal)
                {
                    ["reviewer@anthropic-tools"] = new PluginConfigSettings
                    {
                        Options = new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["style"] = "strict"
                        }
                    }
                }
            };
            var secureStorage = new InMemorySecureStorage(
                new McpSecureStorageData(
                    PluginSecrets: new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
                    {
                        ["reviewer@anthropic-tools"] = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["apiKey"] = "top-secret"
                        }
                    }));
            var optionService = new PluginOptionService(secureStorage, Path.Combine(tempRoot, "plugins"));
            var executor = new HookExecutor(pluginOptionService: optionService);
            var hook = new HookDefinition(
                HookEvent.PreToolUse,
                "plugin",
                null,
                new HookCommandDefinition(
                    HookKind.Command,
                    Command: "printf '%s\\n' \"$CLAUDE_PLUGIN_ROOT\" \"$CLAUDE_PLUGIN_DATA\" \"$CLAUDE_PLUGIN_OPTION_STYLE\"; pwd -W",
                    Shell: HookShell.Bash,
                    TimeoutSeconds: 5),
                PluginId: "reviewer@anthropic-tools",
                PluginRoot: pluginRoot);

            var result = await executor.ExecuteAsync(
                [hook],
                new HookExecutionRequest(
                    HookEvent.PreToolUse,
                    tempRoot,
                    """{"ok":true}""",
                    Settings: settings));

            var trace = Assert.Single(result.Traces);
            Assert.True(trace.Succeeded, trace.Stderr);
            Assert.Contains(WindowsPathConversion.WindowsPathToPosixPath(pluginRoot), trace.Stdout, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(
                WindowsPathConversion.WindowsPathToPosixPath(optionService.GetPluginDataDirectory("reviewer@anthropic-tools")),
                trace.Stdout,
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains("strict", trace.Stdout, StringComparison.Ordinal);
            Assert.Contains(
                Path.GetFullPath(tempRoot).Replace('\\', '/'),
                trace.Stdout.Replace('\\', '/'),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task HookExecutor_On_Linux_Bash_Uses_Native_Plugin_Paths_And_Working_Directory()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var shellPath = await BashShellDetection.FindSuitableShellAsync();
        if (string.IsNullOrWhiteSpace(shellPath))
        {
            return;
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "clawsharp-hook-linux-bash", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var pluginRoot = Path.Combine(tempRoot, "plugin-root");
            Directory.CreateDirectory(pluginRoot);
            var settings = new ClawSharpSettings
            {
                PluginConfigs = new Dictionary<string, PluginConfigSettings>(StringComparer.Ordinal)
                {
                    ["reviewer@anthropic-tools"] = new PluginConfigSettings
                    {
                        Options = new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["style"] = "strict"
                        }
                    }
                }
            };
            var secureStorage = new InMemorySecureStorage(
                new McpSecureStorageData(
                    PluginSecrets: new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
                    {
                        ["reviewer@anthropic-tools"] = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["apiKey"] = "top-secret"
                        }
                    }));
            var optionService = new PluginOptionService(secureStorage, Path.Combine(tempRoot, "plugins"));
            var executor = new HookExecutor(pluginOptionService: optionService);
            var hook = new HookDefinition(
                HookEvent.PreToolUse,
                "plugin",
                null,
                new HookCommandDefinition(
                    HookKind.Command,
                    Command: "printf '%s\\n' \"$CLAUDE_PLUGIN_ROOT\" \"$CLAUDE_PLUGIN_DATA\" \"$CLAUDE_PLUGIN_OPTION_STYLE\"; pwd",
                    Shell: HookShell.Bash,
                    TimeoutSeconds: 5),
                PluginId: "reviewer@anthropic-tools",
                PluginRoot: pluginRoot);

            var result = await executor.ExecuteAsync(
                [hook],
                new HookExecutionRequest(
                    HookEvent.PreToolUse,
                    tempRoot,
                    """{"ok":true}""",
                    Settings: settings));

            var trace = Assert.Single(result.Traces);
            Assert.True(trace.Succeeded, trace.Stderr);
            Assert.Contains(pluginRoot.Replace('\\', '/'), trace.Stdout.Replace('\\', '/'), StringComparison.Ordinal);
            Assert.Contains(
                optionService.GetPluginDataDirectory("reviewer@anthropic-tools").Replace('\\', '/'),
                trace.Stdout.Replace('\\', '/'),
                StringComparison.Ordinal);
            Assert.Contains("strict", trace.Stdout, StringComparison.Ordinal);
            Assert.Contains(
                Path.GetFullPath(tempRoot).Replace('\\', '/'),
                trace.Stdout.Replace('\\', '/'),
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task HookExecutor_StreamAsync_Emits_Hook_Progress_And_Success_Attachment_For_Http_Hook()
    {
        using var client = new HttpClient(new StaticHttpMessageHandler(HttpStatusCode.OK, "{}"));
        var executor = new HookExecutor(client);
        var hook = new HookDefinition(
            HookEvent.Stop,
            "settings",
            null,
            new HookCommandDefinition(HookKind.Http, Url: "https://example.test/hook", TimeoutSeconds: 5, StatusMessage: "Running stop hook"));

        var updates = new List<HookExecutionUpdate>();
        await foreach (var update in executor.StreamAsync(
                           [hook],
                           new HookExecutionRequest(
                               HookEvent.Stop,
                               Environment.CurrentDirectory,
                               """{"ok":true}""",
                               HookName: "Stop",
                               ToolUseId: "hook-stop-1")))
        {
            updates.Add(update);
        }

        var progressMessage = Assert.IsType<ChatMessage>(updates[0].Message);
        var progressBlock = Assert.Single(progressMessage.ContentBlocks);
        Assert.Equal(MessageContentKind.Progress, progressBlock.Kind);
        Assert.Contains("\"type\":\"hook_progress\"", progressBlock.Value, StringComparison.Ordinal);
        Assert.Contains("\"hookName\":\"Stop\"", progressBlock.Value, StringComparison.Ordinal);
        Assert.Contains("\"command\":\"Running stop hook\"", progressBlock.Value, StringComparison.Ordinal);

        var trace = Assert.IsType<HookExecutionTrace>(updates[1].Trace);
        Assert.True(trace.Succeeded);

        var successMessage = Assert.IsType<ChatMessage>(updates[2].Message);
        Assert.Equal("hook_success", Assert.Single(successMessage.ContentBlocks).Metadata!["attachmentType"]);
        Assert.True(QueryAttachmentHelpers.TryGetHookSuccessAttachment(successMessage, out var successAttachment));
        Assert.NotNull(successAttachment);
        Assert.Equal("Stop", successAttachment!.HookName);
        Assert.Equal("hook-stop-1", successAttachment.ToolUseId);
        Assert.Equal(HookEvent.Stop, successAttachment.HookEvent);
        Assert.Equal(string.Empty, successAttachment.Content);
        Assert.Equal("Running stop hook", successAttachment.Command);
    }

    [Fact]
    public async Task HookExecutor_StreamAsync_Emits_Blocking_Error_For_Command_ExitCode_Two()
    {
        var shell = OperatingSystem.IsWindows() ? HookShell.PowerShell : HookShell.Bash;
        var shellPath = shell == HookShell.PowerShell
            ? await PowerShellDetection.GetCachedPowerShellPathAsync()
            : await BashShellDetection.FindSuitableShellAsync();
        if (string.IsNullOrWhiteSpace(shellPath))
        {
            return;
        }

        var command = shell == HookShell.PowerShell
            ? "Write-Error 'blocked'; exit 2"
            : "echo blocked 1>&2; exit 2";
        var executor = new HookExecutor();
        var hook = new HookDefinition(
            HookEvent.Stop,
            "settings",
            null,
            new HookCommandDefinition(HookKind.Command, Command: command, Shell: shell, TimeoutSeconds: 5));

        var updates = new List<HookExecutionUpdate>();
        await foreach (var update in executor.StreamAsync(
                           [hook],
                           new HookExecutionRequest(
                               HookEvent.Stop,
                               Environment.CurrentDirectory,
                               """{"ok":true}""",
                               HookName: "Stop",
                               ToolUseId: "hook-stop-2")))
        {
            updates.Add(update);
        }

        Assert.NotNull(updates[0].Message);
        var trace = Assert.IsType<HookExecutionTrace>(updates[1].Trace);
        Assert.False(trace.Succeeded);
        Assert.Equal(2, trace.ExitCode);

        var blocking = Assert.IsType<HookBlockingError>(updates[2].BlockingError);
        Assert.Contains($"[{command}]", blocking.BlockingError, StringComparison.Ordinal);
        Assert.Equal(command, blocking.Command);
    }

    [Fact]
    public async Task HookExecutor_StreamAsync_Processes_Sync_Stop_Hook_Json_Output()
    {
        var shell = OperatingSystem.IsWindows() ? HookShell.PowerShell : HookShell.Bash;
        var shellPath = shell == HookShell.PowerShell
            ? await PowerShellDetection.GetCachedPowerShellPathAsync()
            : await BashShellDetection.FindSuitableShellAsync();
        if (string.IsNullOrWhiteSpace(shellPath))
        {
            return;
        }

        var json = """{"continue":false,"stopReason":"Stopped by hook","systemMessage":"Stop warning"}""";
        var command = shell == HookShell.PowerShell
            ? $"Write-Output '{json}'"
            : $"printf '%s\\n' '{json}'";
        var executor = new HookExecutor();
        var hook = new HookDefinition(
            HookEvent.Stop,
            "settings",
            null,
            new HookCommandDefinition(HookKind.Command, Command: command, Shell: shell, TimeoutSeconds: 5));

        var updates = new List<HookExecutionUpdate>();
        await foreach (var update in executor.StreamAsync(
                           [hook],
                           new HookExecutionRequest(
                               HookEvent.Stop,
                               Environment.CurrentDirectory,
                               """{"ok":true}""",
                               HookName: "Stop",
                               ToolUseId: "hook-stop-json-1")))
        {
            updates.Add(update);
        }

        Assert.NotNull(updates[0].Message);
        Assert.NotNull(updates[1].Trace);

        Assert.True(updates[2].PreventContinuation);
        Assert.Equal("Stopped by hook", updates[2].StopReason);

        var successMessage = Assert.IsType<ChatMessage>(updates[3].Message);
        Assert.True(QueryAttachmentHelpers.TryGetHookSuccessAttachment(successMessage, out var successAttachment));
        Assert.NotNull(successAttachment);
        Assert.Equal(string.Empty, successAttachment!.Content);

        var systemMessage = Assert.IsType<ChatMessage>(updates[4].Message);
        Assert.True(QueryAttachmentHelpers.TryGetHookSystemMessageAttachment(systemMessage, out var systemAttachment));
        Assert.Equal("Stop warning", systemAttachment!.Content);
    }

    [Fact]
    public async Task HookExecutor_StreamAsync_Processes_Sync_Stop_Hook_Block_Decision_Json_Output()
    {
        var shell = OperatingSystem.IsWindows() ? HookShell.PowerShell : HookShell.Bash;
        var shellPath = shell == HookShell.PowerShell
            ? await PowerShellDetection.GetCachedPowerShellPathAsync()
            : await BashShellDetection.FindSuitableShellAsync();
        if (string.IsNullOrWhiteSpace(shellPath))
        {
            return;
        }

        var json = """{"decision":"block","reason":"Blocked by hook"}""";
        var command = shell == HookShell.PowerShell
            ? $"Write-Output '{json}'"
            : $"printf '%s\\n' '{json}'";
        var executor = new HookExecutor();
        var hook = new HookDefinition(
            HookEvent.Stop,
            "settings",
            null,
            new HookCommandDefinition(HookKind.Command, Command: command, Shell: shell, TimeoutSeconds: 5));

        var updates = new List<HookExecutionUpdate>();
        await foreach (var update in executor.StreamAsync(
                           [hook],
                           new HookExecutionRequest(
                               HookEvent.Stop,
                               Environment.CurrentDirectory,
                               """{"ok":true}""",
                               HookName: "Stop",
                               ToolUseId: "hook-stop-json-2")))
        {
            updates.Add(update);
        }

        Assert.NotNull(updates[0].Message);
        Assert.NotNull(updates[1].Trace);

        var blockingMessage = Assert.IsType<ChatMessage>(updates[2].Message);
        Assert.True(QueryAttachmentHelpers.TryGetHookBlockingErrorAttachment(blockingMessage, out var blockingAttachment));
        Assert.NotNull(blockingAttachment);
        Assert.Equal("Blocked by hook", blockingAttachment!.BlockingError.BlockingError);
        Assert.Equal(command, blockingAttachment.BlockingError.Command);

        Assert.NotNull(updates[2].BlockingError);
        Assert.Equal("Blocked by hook", updates[2].BlockingError!.BlockingError);
    }

    [Fact]
    public async Task HookExecutor_StreamAsync_Processes_Sync_PostToolUse_Hook_Additional_Context()
    {
        var shell = OperatingSystem.IsWindows() ? HookShell.PowerShell : HookShell.Bash;
        var shellPath = shell == HookShell.PowerShell
            ? await PowerShellDetection.GetCachedPowerShellPathAsync()
            : await BashShellDetection.FindSuitableShellAsync();
        if (string.IsNullOrWhiteSpace(shellPath))
        {
            return;
        }

        var json = """{"hookSpecificOutput":{"hookEventName":"PostToolUse","additionalContext":"Review this output"}}""";
        var command = shell == HookShell.PowerShell
            ? $"Write-Output '{json}'"
            : $"printf '%s\\n' '{json}'";
        var executor = new HookExecutor();
        var hook = new HookDefinition(
            HookEvent.PostToolUse,
            "settings",
            null,
            new HookCommandDefinition(HookKind.Command, Command: command, Shell: shell, TimeoutSeconds: 5));

        var updates = new List<HookExecutionUpdate>();
        await foreach (var update in executor.StreamAsync(
                           [hook],
                           new HookExecutionRequest(
                               HookEvent.PostToolUse,
                               Environment.CurrentDirectory,
                               """{"ok":true}""",
                               HookName: "PostToolUse",
                               ToolUseId: "hook-posttool-json-1")))
        {
            updates.Add(update);
        }

        Assert.NotNull(updates[0].Message);
        Assert.NotNull(updates[1].Trace);

        var successMessage = Assert.IsType<ChatMessage>(updates[2].Message);
        Assert.True(QueryAttachmentHelpers.TryGetHookSuccessAttachment(successMessage, out _));

        var additionalContextMessage = Assert.IsType<ChatMessage>(updates[3].Message);
        Assert.True(QueryAttachmentHelpers.TryGetHookAdditionalContextAttachment(additionalContextMessage, out var additionalContext));
        Assert.NotNull(additionalContext);
        Assert.Equal(["Review this output"], additionalContext!.Content);
    }

    [Fact]
    public async Task HookExecutor_StreamAsync_Maps_Sync_Hook_Event_Mismatch_To_Non_Blocking_Error()
    {
        var shell = OperatingSystem.IsWindows() ? HookShell.PowerShell : HookShell.Bash;
        var shellPath = shell == HookShell.PowerShell
            ? await PowerShellDetection.GetCachedPowerShellPathAsync()
            : await BashShellDetection.FindSuitableShellAsync();
        if (string.IsNullOrWhiteSpace(shellPath))
        {
            return;
        }

        var json = """{"hookSpecificOutput":{"hookEventName":"UserPromptSubmit","additionalContext":"wrong event"}}""";
        var command = shell == HookShell.PowerShell
            ? $"Write-Output '{json}'"
            : $"printf '%s\\n' '{json}'";
        var executor = new HookExecutor();
        var hook = new HookDefinition(
            HookEvent.PostToolUse,
            "settings",
            null,
            new HookCommandDefinition(HookKind.Command, Command: command, Shell: shell, TimeoutSeconds: 5));

        var updates = new List<HookExecutionUpdate>();
        await foreach (var update in executor.StreamAsync(
                           [hook],
                           new HookExecutionRequest(
                               HookEvent.PostToolUse,
                               Environment.CurrentDirectory,
                               """{"ok":true}""",
                               HookName: "PostToolUse",
                               ToolUseId: "hook-posttool-json-2")))
        {
            updates.Add(update);
        }

        Assert.NotNull(updates[0].Message);
        Assert.NotNull(updates[1].Trace);

        var errorMessage = Assert.IsType<ChatMessage>(updates[2].Message);
        Assert.True(QueryAttachmentHelpers.TryGetHookNonBlockingErrorAttachment(errorMessage, out var errorAttachment));
        Assert.NotNull(errorAttachment);
        Assert.Contains("Hook returned incorrect event name", errorAttachment!.Stderr, StringComparison.Ordinal);
    }

    private sealed class StaticHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _content;

        public StaticHttpMessageHandler(HttpStatusCode statusCode, string content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content)
            });
        }
    }

    private sealed class InMemorySecureStorage : IMcpSecureStorage
    {
        private McpSecureStorageData? _data;

        public InMemorySecureStorage(McpSecureStorageData? data)
        {
            _data = data;
        }

        public McpSecureStorageData? Read() => _data;

        public Task<McpSecureStorageData?> ReadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_data);
        }

        public void Update(McpSecureStorageData data)
        {
            _data = data;
        }

        public bool Delete()
        {
            _data = null;
            return true;
        }
    }
}
