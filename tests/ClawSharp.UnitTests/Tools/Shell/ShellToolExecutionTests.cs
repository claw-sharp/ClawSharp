using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Tasks;
using ClawSharp.Tools;

namespace ClawSharp.UnitTests;

public sealed class ShellToolExecutionTests
{
    [Fact]
    public async Task ToolRegistry_Registers_Bash_And_PowerShell_Tools_With_Ts_Shaped_Schemas()
    {
        await WithPowerShellToolEnabledAsync(() =>
        {
            var registry = new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry());

            var bash = Assert.Single(registry.All, tool => tool.Name == "Bash");
            Assert.Equal("object", bash.InputSchema?["type"]?.GetValue<string>());
            Assert.Equal("object", bash.OutputSchema?["type"]?.GetValue<string>());
            Assert.Equal("execute shell commands", bash.SearchHint);

            if (OperatingSystem.IsWindows())
            {
                var powerShell = Assert.Single(registry.All, tool => tool.Name == "PowerShell");
                Assert.Equal("object", powerShell.InputSchema?["type"]?.GetValue<string>());
                Assert.Equal("object", powerShell.OutputSchema?["type"]?.GetValue<string>());
                Assert.Equal("execute Windows PowerShell commands", powerShell.SearchHint);
            }
            else
            {
                Assert.DoesNotContain(registry.All, tool => tool.Name == "PowerShell");
            }

            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task BashTool_Executes_RunInBackground_And_Queues_Completion_Notification()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-bash-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var shellPath = await BashShellDetection.FindSuitableShellAsync();
            if (string.IsNullOrWhiteSpace(shellPath))
            {
                return;
            }

            var queue = new InMemoryQueuedCommandQueue();
            var tasks = new TaskRegistry(tempDir, queuedCommandQueue: queue);
            var permissionContext = CreatePermissionContext(
                mode: PermissionMode.BypassPermissions,
                isBypassPermissionsModeAvailable: true);
            var registry = new ToolRegistry(
                tempDir,
                tasks,
                toolPermissionContext: permissionContext,
                appStateStore: CreateAppStateStore(tempDir, permissionContext));
            var session = new DefaultSessionFactory(tempDir).Create();
            var settings = new ClawSharpSettings();

            var backgroundResult = await registry.ExecuteAsync(
                "Bash",
                """{"command":"sleep 1","description":"Sleep briefly","run_in_background":true}""",
                session,
                settings);

            Assert.True(backgroundResult.Success, backgroundResult.Output);
            Assert.Contains("Command running in background", backgroundResult.Output, StringComparison.Ordinal);
            var structured = Assert.IsType<JsonObject>(backgroundResult.StructuredOutput);
            var taskId = structured["backgroundTaskId"]?.GetValue<string>();
            Assert.False(string.IsNullOrWhiteSpace(taskId));
            Assert.NotNull(await WaitForTaskStatusAsync(tasks, taskId!, ClawSharp.Tasks.TaskStatus.Completed));

            var sandboxResult = await registry.ExecuteAsync(
                "Bash",
                """{"command":"echo hi","dangerouslyDisableSandbox":true}""",
                session,
                settings);
            Assert.False(sandboxResult.Success);
            Assert.Contains("Run outside of the sandbox", sandboxResult.Output, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task PowerShellTool_Executes_Command_Through_Runner()
    {
        await WithPowerShellToolEnabledAsync(async () =>
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-powershell-tool-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var powerShellPath = await PowerShellDetection.GetCachedPowerShellPathAsync();
                if (string.IsNullOrWhiteSpace(powerShellPath))
                {
                    return;
                }

                var registry = new ToolRegistry(
                    tempDir,
                    new TaskRegistry(tempDir),
                    toolPermissionContext: CreatePermissionContext(
                        mode: PermissionMode.BypassPermissions,
                        isBypassPermissionsModeAvailable: true));
                var session = new DefaultSessionFactory(tempDir).Create();
                var settings = new ClawSharpSettings();

                var result = await registry.ExecuteAsync(
                    "PowerShell",
                    """{"command":"Write-Output 'hello from powershell'"}""",
                    session,
                    settings);

                Assert.True(result.Success, result.Output);
                var structured = Assert.IsType<JsonObject>(result.StructuredOutput);
                Assert.Contains("hello from powershell", structured["stdout"]?.GetValue<string>() ?? string.Empty, StringComparison.Ordinal);
                Assert.Equal(string.Empty, structured["stderr"]?.GetValue<string>());
                Assert.False(structured["interrupted"]?.GetValue<bool>() ?? true);
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }
        });
    }

    [Fact]
    public async Task PowerShellTool_Uses_Session_WorkingDirectory_For_Execution()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await WithPowerShellToolEnabledAsync(async () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-powershell-cwd-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var powerShellPath = await PowerShellDetection.GetCachedPowerShellPathAsync();
                if (string.IsNullOrWhiteSpace(powerShellPath))
                {
                    return;
                }

                var registry = new ToolRegistry(
                    tempDir,
                    new TaskRegistry(tempDir),
                    toolPermissionContext: CreatePermissionContext(
                        mode: PermissionMode.BypassPermissions,
                        isBypassPermissionsModeAvailable: true));

                var result = await registry.ExecuteAsync(
                    "PowerShell",
                    """{"command":"Write-Output (Get-Location).Path"}""",
                    new DefaultSessionFactory(tempDir).Create(),
                    new ClawSharpSettings());

                Assert.True(result.Success, result.Output);
                var structured = Assert.IsType<JsonObject>(result.StructuredOutput);
                var stdout = structured["stdout"]?.GetValue<string>() ?? string.Empty;
                Assert.Contains(
                    Path.GetFullPath(tempDir),
                    stdout,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }
        });
    }

    [Fact]
    public async Task PowerShellTool_RunInBackground_Queues_Completion_Notification()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await WithPowerShellToolEnabledAsync(async () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-powershell-background-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var powerShellPath = await PowerShellDetection.GetCachedPowerShellPathAsync();
                if (string.IsNullOrWhiteSpace(powerShellPath))
                {
                    return;
                }

                var tasks = new TaskRegistry(tempDir);
                var registry = new ToolRegistry(
                    tempDir,
                    tasks,
                    toolPermissionContext: CreatePermissionContext(
                        mode: PermissionMode.BypassPermissions,
                        isBypassPermissionsModeAvailable: true));

                var result = await registry.ExecuteAsync(
                    "PowerShell",
                    """{"command":"Start-Sleep -Seconds 1","description":"Sleep briefly","run_in_background":true}""",
                    new DefaultSessionFactory(tempDir).Create(),
                    new ClawSharpSettings());

                Assert.True(result.Success, result.Output);
                Assert.Contains("Command running in background", result.Output, StringComparison.Ordinal);
                var structured = Assert.IsType<JsonObject>(result.StructuredOutput);
                var taskId = structured["backgroundTaskId"]?.GetValue<string>();
                Assert.False(string.IsNullOrWhiteSpace(taskId));
                Assert.NotNull(await WaitForTaskStatusAsync(tasks, taskId!, ClawSharp.Tasks.TaskStatus.Completed));
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }
        });
    }

    [Fact]
    public async Task PowerShellTool_AutoBackgrounds_TimedOut_ReplForeground_Command()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await WithPowerShellToolEnabledAsync(async () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-powershell-autobg-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var powerShellPath = await PowerShellDetection.GetCachedPowerShellPathAsync();
                if (string.IsNullOrWhiteSpace(powerShellPath))
                {
                    return;
                }

                var tasks = new TaskRegistry(tempDir);
                var registry = new ToolRegistry(
                    tempDir,
                    tasks,
                    toolPermissionContext: CreatePermissionContext(
                        mode: PermissionMode.BypassPermissions,
                        isBypassPermissionsModeAvailable: true));

                var result = await registry.ExecuteAsync(
                    "PowerShell",
                    """{"command":"Write-Output 'before'; ping -n 2 127.0.0.1 > $null","timeout":100}""",
                    new DefaultSessionFactory(tempDir).Create(),
                    new ClawSharpSettings(),
                    querySource: "repl_main_thread");

                Assert.True(result.Success, result.Output);
                var structured = Assert.IsType<JsonObject>(result.StructuredOutput);
                var taskId = structured["backgroundTaskId"]?.GetValue<string>();
                Assert.False(string.IsNullOrWhiteSpace(taskId));
                Assert.True(structured["assistantAutoBackgrounded"]?.GetValue<bool>() ?? false);
                Assert.Contains("assistant-mode blocking budget", result.Output, StringComparison.Ordinal);
                Assert.NotNull(await WaitForTaskStatusAsync(tasks, taskId!, ClawSharp.Tasks.TaskStatus.Completed));
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }
        });
    }

    [Fact]
    public async Task PowerShellTool_Uses_LastExitCode_For_Native_Command_With_Redirected_Stderr()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await WithPowerShellToolEnabledAsync(async () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-powershell-lastexitcode-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var powerShellPath = await PowerShellDetection.GetCachedPowerShellPathAsync();
                if (string.IsNullOrWhiteSpace(powerShellPath))
                {
                    return;
                }

                var registry = new ToolRegistry(
                    tempDir,
                    new TaskRegistry(tempDir),
                    toolPermissionContext: CreatePermissionContext(
                        mode: PermissionMode.BypassPermissions,
                        isBypassPermissionsModeAvailable: true));

                var result = await registry.ExecuteAsync(
                    "PowerShell",
                    """{"command":"cmd /c \"echo native-stderr 1>&2\" 2>&1"}""",
                    new DefaultSessionFactory(tempDir).Create(),
                    new ClawSharpSettings());

                Assert.True(result.Success, result.Output);
                var structured = Assert.IsType<JsonObject>(result.StructuredOutput);
                Assert.Contains("native-stderr", structured["stdout"]?.GetValue<string>() ?? string.Empty, StringComparison.Ordinal);
                Assert.Equal(string.Empty, structured["stderr"]?.GetValue<string>());
                Assert.False(structured["interrupted"]?.GetValue<bool>() ?? true);
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }
        });
    }

    [Fact]
    public async Task PowerShellTool_Interprets_Findstr_NoMatch_As_Success_Through_Real_Runtime()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await WithPowerShellToolEnabledAsync(async () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-powershell-findstr-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var powerShellPath = await PowerShellDetection.GetCachedPowerShellPathAsync();
                if (string.IsNullOrWhiteSpace(powerShellPath))
                {
                    return;
                }

                File.WriteAllText(Path.Combine(tempDir, "sample.txt"), "alpha");

                var registry = new ToolRegistry(
                    tempDir,
                    new TaskRegistry(tempDir),
                    toolPermissionContext: CreatePermissionContext(
                        mode: PermissionMode.BypassPermissions,
                        isBypassPermissionsModeAvailable: true));

                var result = await registry.ExecuteAsync(
                    "PowerShell",
                    """{"command":"findstr beta sample.txt"}""",
                    new DefaultSessionFactory(tempDir).Create(),
                    new ClawSharpSettings());

                Assert.True(result.Success, result.Output);
                var structured = Assert.IsType<JsonObject>(result.StructuredOutput);
                Assert.Equal("No matches found", structured["returnCodeInterpretation"]?.GetValue<string>());
                Assert.False(structured["interrupted"]?.GetValue<bool>() ?? true);
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }
        });
    }

    [Fact]
    public async Task PowerShellTool_Uses_PermissionPrompter_And_Caches_AlwaysAllow_For_Exact_Command()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await WithPowerShellToolEnabledAsync(async () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-powershell-prompt-cache-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var powerShellPath = await PowerShellDetection.GetCachedPowerShellPathAsync();
                if (string.IsNullOrWhiteSpace(powerShellPath))
                {
                    return;
                }

                var prompt = new TestPermissionPrompter(PromptPermissionDecision.AlwaysAllow);
                var registry = new ToolRegistry(
                    tempDir,
                    new TaskRegistry(tempDir),
                    permissionPrompter: prompt);
                var command = $"Write-Output 'prompt-cache-{Guid.NewGuid():N}'";

                var firstResult = await registry.ExecuteAsync(
                    "PowerShell",
                    $$"""{"command":"{{command}}"}""",
                    new DefaultSessionFactory(tempDir).Create(),
                    new ClawSharpSettings());
                var secondResult = await registry.ExecuteAsync(
                    "PowerShell",
                    $$"""{"command":"{{command}}"}""",
                    new DefaultSessionFactory(tempDir).Create(),
                    new ClawSharpSettings());

                Assert.True(firstResult.Success, firstResult.Output);
                Assert.True(secondResult.Success, secondResult.Output);
                Assert.Equal(1, prompt.CallCount);
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }
        });
    }

    [Fact]
    public async Task PowerShellTool_AlwaysAllow_Remembers_Tool_For_Session_After_Runtime_Cache_Reset()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await WithPowerShellToolEnabledAsync(async () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-powershell-session-allow-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var powerShellPath = await PowerShellDetection.GetCachedPowerShellPathAsync();
                if (string.IsNullOrWhiteSpace(powerShellPath))
                {
                    return;
                }

                var prompt = new TestPermissionPrompter(PromptPermissionDecision.AlwaysAllow);
                var permissionContext = CreatePermissionContext();
                var appStateStore = CreateAppStateStore(tempDir, permissionContext);
                var registry = new ToolRegistry(
                    tempDir,
                    new TaskRegistry(tempDir),
                    toolPermissionContext: permissionContext,
                    appStateStore: appStateStore,
                    permissionPrompter: prompt);

                var firstResult = await registry.ExecuteAsync(
                    "PowerShell",
                    $$"""{"command":"Write-Output 'session-allow-1-{{Guid.NewGuid():N}}'"}""",
                    new DefaultSessionFactory(tempDir).Create(),
                    new ClawSharpSettings());

                Assert.True(firstResult.Success, firstResult.Output);
                Assert.Equal(["PowerShell"], appStateStore.GetState().ToolPermissionContext.AlwaysAllowRules[PermissionRuleSource.Session]);

                ResetShellPermissionRuntimeSessionCache();

                var secondResult = await registry.ExecuteAsync(
                    "PowerShell",
                    $$"""{"command":"Write-Output 'session-allow-2-{{Guid.NewGuid():N}}'"}""",
                    new DefaultSessionFactory(tempDir).Create(),
                    new ClawSharpSettings());

                Assert.True(secondResult.Success, secondResult.Output);
                Assert.Equal(1, prompt.CallCount);
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }
        });
    }

    [Fact]
    public async Task PowerShellTool_Dangerous_Allow_Rule_In_Auto_Mode_Still_Prompts()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await WithPowerShellToolEnabledAsync(async () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-powershell-dangerous-allow-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var powerShellPath = await PowerShellDetection.GetCachedPowerShellPathAsync();
                if (string.IsNullOrWhiteSpace(powerShellPath))
                {
                    return;
                }

                var prompt = new TestPermissionPrompter(PromptPermissionDecision.Deny);
                var registry = new ToolRegistry(
                    tempDir,
                    new TaskRegistry(tempDir),
                    toolPermissionContext: CreatePermissionContext(
                        mode: PermissionMode.Auto,
                        alwaysAllowRules: new Dictionary<PermissionRuleSource, IReadOnlyList<string>>
                        {
                            [PermissionRuleSource.Session] = ["PowerShell(invoke-webrequest:*)"]
                        }),
                    permissionPrompter: prompt);

                var result = await registry.ExecuteAsync(
                    "PowerShell",
                    """{"command":"Invoke-WebRequest https://example.invalid"}""",
                    new DefaultSessionFactory(tempDir).Create(),
                    new ClawSharpSettings());

                Assert.False(result.Success);
                Assert.Equal(1, prompt.CallCount);
                Assert.Equal("ClawSharp to use PowerShell, but you haven't granted it yet.", result.Output);
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }
        });
    }

    [Fact]
    public async Task BashTool_Executes_Command_When_Shell_Is_Available()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-bash-exec-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var shellPath = await BashShellDetection.FindSuitableShellAsync();
            if (string.IsNullOrWhiteSpace(shellPath))
            {
                return;
            }

            var permissionContext = CreatePermissionContext(
                mode: PermissionMode.BypassPermissions,
                isBypassPermissionsModeAvailable: true);
            var registry = new ToolRegistry(
                tempDir,
                new TaskRegistry(tempDir),
                toolPermissionContext: permissionContext,
                appStateStore: CreateAppStateStore(tempDir, permissionContext));
            var session = new DefaultSessionFactory(tempDir).Create();
            var settings = new ClawSharpSettings();

            var result = await registry.ExecuteAsync(
                "Bash",
                """{"command":"printf 'hello from bash\n'"}""",
                session,
                settings);

            Assert.True(result.Success, result.Output);
            var structured = Assert.IsType<JsonObject>(result.StructuredOutput);
            Assert.Contains("hello from bash", structured["stdout"]?.GetValue<string>() ?? string.Empty, StringComparison.Ordinal);
            Assert.Equal(string.Empty, structured["stderr"]?.GetValue<string>());
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task BashTool_Uses_Session_WorkingDirectory_For_Execution()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-bash-cwd-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var shellPath = await BashShellDetection.FindSuitableShellAsync();
            if (string.IsNullOrWhiteSpace(shellPath))
            {
                return;
            }

            var permissionContext = CreatePermissionContext(
                mode: PermissionMode.BypassPermissions,
                isBypassPermissionsModeAvailable: true);
            var registry = new ToolRegistry(
                tempDir,
                new TaskRegistry(tempDir),
                toolPermissionContext: permissionContext,
                appStateStore: CreateAppStateStore(tempDir, permissionContext));
            var command = OperatingSystem.IsWindows() ? "pwd -W" : "pwd";

            var result = await registry.ExecuteAsync(
                "Bash",
                $$"""{"command":"{{command}}"}""",
                new DefaultSessionFactory(tempDir).Create(),
                new ClawSharpSettings());

            Assert.True(result.Success, result.Output);
            var structured = Assert.IsType<JsonObject>(result.StructuredOutput);
            var stdout = structured["stdout"]?.GetValue<string>() ?? string.Empty;
            Assert.Contains(
                Path.GetFullPath(tempDir).Replace('\\', '/'),
                stdout.Replace('\\', '/'),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task BashTool_Default_Mode_Requests_Permission_Without_Allow_Rule()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-bash-permission-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var registry = new ToolRegistry(tempDir, new TaskRegistry(tempDir));
            var session = new DefaultSessionFactory(tempDir).Create();

            var result = await registry.ExecuteAsync(
                "Bash",
                """{"command":"echo hi"}""",
                session,
                new ClawSharpSettings());

            Assert.False(result.Success);
            Assert.Equal("ClawSharp to use Bash, but you haven't granted it yet.", result.Output);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task BashTool_Allow_Rule_Allows_Command_Execution()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-bash-allow-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var shellPath = await BashShellDetection.FindSuitableShellAsync();
            if (string.IsNullOrWhiteSpace(shellPath))
            {
                return;
            }

            var permissionContext = CreatePermissionContext(
                alwaysAllowRules: new Dictionary<PermissionRuleSource, IReadOnlyList<string>>
                {
                    [PermissionRuleSource.Session] = ["Bash(echo:*)"]
                });
            var registry = new ToolRegistry(
                tempDir,
                new TaskRegistry(tempDir),
                toolPermissionContext: permissionContext,
                appStateStore: CreateAppStateStore(tempDir, permissionContext));

            var result = await registry.ExecuteAsync(
                "Bash",
                """{"command":"echo hello"}""",
                new DefaultSessionFactory(tempDir).Create(),
                new ClawSharpSettings());

            Assert.True(result.Success, result.Output);
            Assert.Contains("hello", result.Output, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task BashTool_Uses_PermissionPrompter_For_Promptable_Command()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-bash-prompt-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var shellPath = await BashShellDetection.FindSuitableShellAsync();
            if (string.IsNullOrWhiteSpace(shellPath))
            {
                return;
            }

            var prompt = new TestPermissionPrompter(PromptPermissionDecision.Allow);
            var registry = new ToolRegistry(
                tempDir,
                new TaskRegistry(tempDir),
                permissionPrompter: prompt);
            var command = $"echo prompt-{Guid.NewGuid():N}";

            var result = await registry.ExecuteAsync(
                "Bash",
                $$"""{"command":"{{command}}"}""",
                new DefaultSessionFactory(tempDir).Create(),
                new ClawSharpSettings());

            Assert.True(result.Success, result.Output);
            Assert.Equal(1, prompt.CallCount);
            Assert.Contains("prompt-", result.Output, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task BashTool_Persists_Large_Output_Into_ToolResults()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-bash-persisted-output-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var shellPath = await BashShellDetection.FindSuitableShellAsync();
            if (string.IsNullOrWhiteSpace(shellPath))
            {
                return;
            }

            var permissionContext = CreatePermissionContext(
                mode: PermissionMode.BypassPermissions,
                isBypassPermissionsModeAvailable: true);
            var registry = new ToolRegistry(
                tempDir,
                new TaskRegistry(tempDir),
                toolPermissionContext: permissionContext,
                appStateStore: CreateAppStateStore(tempDir, permissionContext));
            var session = new DefaultSessionFactory(tempDir).Create();

            var result = await registry.ExecuteAsync(
                "Bash",
                """{"command":"yes 'persisted-line' | head -n 700000"}""",
                session,
                new ClawSharpSettings());

            Assert.True(result.Success, result.Output);
            Assert.Contains("<persisted-output>", result.Output, StringComparison.Ordinal);

            var structured = Assert.IsType<JsonObject>(result.StructuredOutput);
            var persistedPath = structured["persistedOutputPath"]?.GetValue<string>();
            Assert.False(string.IsNullOrWhiteSpace(persistedPath));
            Assert.True(File.Exists(persistedPath));
            Assert.True((structured["persistedOutputSize"]?.GetValue<long>() ?? 0) > 0);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task BashTool_AutoBackgrounds_TimedOut_ReplForeground_Command()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-bash-autobg-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var shellPath = await BashShellDetection.FindSuitableShellAsync();
            if (string.IsNullOrWhiteSpace(shellPath))
            {
                return;
            }

            var tasks = new TaskRegistry(tempDir);
            var permissionContext = CreatePermissionContext(
                mode: PermissionMode.BypassPermissions,
                isBypassPermissionsModeAvailable: true);
            var registry = new ToolRegistry(
                tempDir,
                tasks,
                toolPermissionContext: permissionContext,
                appStateStore: CreateAppStateStore(tempDir, permissionContext));
            var session = new DefaultSessionFactory(tempDir).Create();

            var result = await registry.ExecuteAsync(
                "Bash",
                """{"command":"sh -c 'sleep 1'","timeout":100}""",
                session,
                new ClawSharpSettings(),
                querySource: "repl_main_thread");

            Assert.True(result.Success, result.Output);
            var structured = Assert.IsType<JsonObject>(result.StructuredOutput);
            var taskId = structured["backgroundTaskId"]?.GetValue<string>();
            Assert.False(string.IsNullOrWhiteSpace(taskId));
            Assert.True(structured["assistantAutoBackgrounded"]?.GetValue<bool>() ?? false);
            Assert.Contains("assistant-mode blocking budget", result.Output, StringComparison.Ordinal);
            Assert.NotNull(await WaitForTaskStatusAsync(tasks, taskId!, ClawSharp.Tasks.TaskStatus.Completed));
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task PowerShellTool_Deny_Rule_Blocks_Command()
    {
        await WithPowerShellToolEnabledAsync(async () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-powershell-deny-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var registry = new ToolRegistry(
                    tempDir,
                    new TaskRegistry(tempDir),
                    toolPermissionContext: CreatePermissionContext(
                        alwaysDenyRules: new Dictionary<PermissionRuleSource, IReadOnlyList<string>>
                        {
                            [PermissionRuleSource.Session] = ["PowerShell(Write-Output:*)"]
                        }));

                var result = await registry.ExecuteAsync(
                    "PowerShell",
                    """{"command":"Write-Output 'blocked'"}""",
                    new DefaultSessionFactory(tempDir).Create(),
                    new ClawSharpSettings());

                Assert.False(result.Success);
                Assert.Equal("Permission to use PowerShell has been denied.", result.Output);
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }
        });
    }

    [Fact]
    public async Task PowerShellTool_Refuses_Native_Windows_When_Sandbox_Is_Required_By_Policy()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await WithPowerShellToolEnabledAsync(async () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-powershell-sandbox-policy-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var registry = new ToolRegistry(
                    tempDir,
                    new TaskRegistry(tempDir),
                    toolPermissionContext: CreatePermissionContext(
                        mode: PermissionMode.BypassPermissions,
                        isBypassPermissionsModeAvailable: true,
                        isSandboxEnabledInSettings: true,
                        areUnsandboxedCommandsAllowed: false));

                var result = await registry.ExecuteAsync(
                    "PowerShell",
                    """{"command":"Write-Output 'blocked by policy'"}""",
                    new DefaultSessionFactory(tempDir).Create(),
                    new ClawSharpSettings());

                Assert.False(result.Success);
                Assert.Equal(
                    "Enterprise policy requires sandboxing, but sandboxing is not available on native Windows. Shell command execution is blocked on this platform by policy.",
                    result.Output);
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }
        });
    }

    [Fact]
    public async Task PowerShellTool_ExecuteAsync_DirectCall_Also_Refuses_Native_Windows_When_Sandbox_Is_Required_By_Policy()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await WithPowerShellToolEnabledAsync(async () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-powershell-direct-sandbox-policy-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var tool = CreatePowerShellTool();
                var context = new ToolExecutionContext(
                    """{"command":"Write-Output 'blocked by policy'"}""",
                    tempDir,
                    new DefaultSessionFactory(tempDir).Create(),
                    new NullClawSharpAppStateStore(tempDir),
                    new TaskRegistry(tempDir),
                    new TaskRegistry(tempDir),
                    new ClawSharpSettings(),
                    FileStateCache.CreateWithSizeLimit(FileStateCache.DefaultMaxEntries),
                    CreatePermissionContext(
                        mode: PermissionMode.BypassPermissions,
                        isBypassPermissionsModeAvailable: true,
                        isSandboxEnabledInSettings: true,
                        areUnsandboxedCommandsAllowed: false),
                    BuiltInAgentDefinitions.GetBuiltInAgents(),
                    new NullFileUpdateNotifier(),
                    new TestPermissionPrompter(PromptPermissionDecision.Allow));

                var result = await tool.ExecuteAsync(context);

                Assert.False(result.Success);
                Assert.Equal(
                    "Enterprise policy requires sandboxing, but sandboxing is not available on native Windows. Shell command execution is blocked on this platform by policy.",
                    result.Output);
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }
        });
    }

    [Theory]
    [InlineData("Start-Sleep -Seconds 1", false)]
    [InlineData("sleep 1", false)]
    [InlineData("Write-Output 'hello'; sleep 1", false)]
    [InlineData("Get-Date", true)]
    public void PowerShell_Autobackgrounding_Guard_Matches_Ts_Sleep_Alias_Rules(string command, bool expected)
    {
        var toolsAssembly = typeof(ToolRegistry).Assembly;
        var shellToolKindType = toolsAssembly.GetType("ClawSharp.Tools.ShellToolKind", throwOnError: true)!;
        var semanticsType = toolsAssembly.GetType("ClawSharp.Tools.ShellCommandSemantics", throwOnError: true)!;
        var powerShellKind = Enum.Parse(shellToolKindType, "PowerShell");
        var isAutoBackgroundingAllowed = semanticsType.GetMethod(
            "IsAutoBackgroundingAllowed",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)!;

        var actual = Assert.IsType<bool>(isAutoBackgroundingAllowed.Invoke(null, [powerShellKind, command]));
        Assert.Equal(expected, actual);
    }

    private static readonly SemaphoreSlim PowerShellToolEnvironmentLock = new(1, 1);

    private static ToolPermissionContext CreatePermissionContext(
        PermissionMode mode = PermissionMode.Default,
        bool isBypassPermissionsModeAvailable = false,
        IReadOnlyDictionary<PermissionRuleSource, IReadOnlyList<string>>? alwaysAllowRules = null,
        IReadOnlyDictionary<PermissionRuleSource, IReadOnlyList<string>>? alwaysDenyRules = null,
        IReadOnlyDictionary<PermissionRuleSource, IReadOnlyList<string>>? alwaysAskRules = null,
        bool isSandboxEnabledInSettings = false,
        bool areUnsandboxedCommandsAllowed = true)
    {
        return new ToolPermissionContext(
            mode,
            new Dictionary<string, AdditionalWorkingDirectory>(StringComparer.Ordinal),
            alwaysAllowRules ?? new Dictionary<PermissionRuleSource, IReadOnlyList<string>>(),
            alwaysDenyRules ?? new Dictionary<PermissionRuleSource, IReadOnlyList<string>>(),
            alwaysAskRules ?? new Dictionary<PermissionRuleSource, IReadOnlyList<string>>(),
            IsBypassPermissionsModeAvailable: isBypassPermissionsModeAvailable,
            IsSandboxEnabledInSettings: isSandboxEnabledInSettings,
            AreUnsandboxedCommandsAllowed: areUnsandboxedCommandsAllowed);
    }

    private static IClawSharpTool CreatePowerShellTool()
    {
        var toolType = typeof(ToolRegistry).Assembly.GetType("ClawSharp.Tools.PowerShellTool", throwOnError: true)!;
        return (IClawSharpTool)Activator.CreateInstance(toolType, nonPublic: true)!;
    }

    private static ClawSharpAppStateStore CreateAppStateStore(string workspaceRoot, ToolPermissionContext permissionContext)
    {
        return new ClawSharpAppStateStore(
            ClawSharpAppState.CreateDefault(
                workspaceRoot,
                StartupEnvironment.Capture(),
                new ClawSharpSettings(),
                [],
                [],
                [],
                [],
                [],
                [],
                permissionContext));
    }

    private static void ResetShellPermissionRuntimeSessionCache()
    {
        var runtimeType = typeof(ToolRegistry).Assembly.GetType("ClawSharp.Tools.ShellToolPermissionRuntime", throwOnError: true)!;
        var resetMethod = runtimeType.GetMethod(
            "ResetSessionCache",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        resetMethod.Invoke(null, null);
    }

    private static async Task<ClawSharpTask?> WaitForTaskStatusAsync(
        TaskRegistry tasks,
        string taskId,
        ClawSharp.Tasks.TaskStatus status)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            if (tasks.TryGet(taskId, out var task) && task?.Status == status)
            {
                return task;
            }

            await Task.Delay(50);
        }

        return null;
    }

    private sealed class TestPermissionPrompter : IPermissionPrompter
    {
        private readonly PromptPermissionDecision _decision;

        public TestPermissionPrompter(PromptPermissionDecision decision)
        {
            _decision = decision;
        }

        public int CallCount { get; private set; }

        public Task<PromptPermissionDecision> PromptAsync(string message, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_decision);
        }
    }

    private static async Task WithPowerShellToolEnabledAsync(Func<Task> action)
    {
        await PowerShellToolEnvironmentLock.WaitAsync();
        var originalUserType = Environment.GetEnvironmentVariable("USER_TYPE");
        var originalGate = Environment.GetEnvironmentVariable("CLAUDE_CODE_USE_POWERSHELL_TOOL");

        try
        {
            Environment.SetEnvironmentVariable("USER_TYPE", "ant");
            Environment.SetEnvironmentVariable("CLAUDE_CODE_USE_POWERSHELL_TOOL", null);
            PowerShellDetection.ResetPowerShellCache();
            if (!OperatingSystem.IsWindows() || await PowerShellDetection.GetCachedPowerShellPathAsync() is null)
            {
                return;
            }
            await action();
        }
        finally
        {
            Environment.SetEnvironmentVariable("USER_TYPE", originalUserType);
            Environment.SetEnvironmentVariable("CLAUDE_CODE_USE_POWERSHELL_TOOL", originalGate);
            PowerShellDetection.ResetPowerShellCache();
            PowerShellToolEnvironmentLock.Release();
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }
}
