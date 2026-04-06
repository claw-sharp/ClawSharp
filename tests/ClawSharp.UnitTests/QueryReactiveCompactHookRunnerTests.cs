// TS parity status: focused C# coverage for the shared compact.ts pre-compact and post-compact hook execution path beneath the reactive-compact runtime; live post-summary invocation from the compaction model-call path remains blocked until the raw summary handoff is ported.
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Query;
using ClawSharp.Tasks;
using ClawSharp.Tools;

namespace ClawSharp.UnitTests;

public sealed class QueryReactiveCompactHookRunnerTests
{
    [Fact]
    public async Task RunPreCompactAsync_Aggregates_Custom_Instructions_And_User_Display_Message()
    {
        var shell = await GetAvailableShellAsync();
        if (shell is null)
        {
            return;
        }

        var root = CreateTempRoot();
        try
        {
            var runner = CreateRunner(
                root,
                [
                    CreateHook(
                        HookEvent.PreCompact,
                        "manual",
                        shell.Value,
                        CreateEchoCommand(shell.Value, "alpha"),
                        "settings"),
                    CreateHook(
                        HookEvent.PreCompact,
                        "manual",
                        shell.Value,
                        CreateEchoCommand(shell.Value, "beta"),
                        "plugin")
                ]);

            var result = await runner.RunPreCompactAsync(CreateContext(root));

            Assert.Equal("alpha\n\nbeta", result.CustomInstructions);
            Assert.Equal(
                $"PreCompact [{CreateEchoCommand(shell.Value, "alpha")}] completed successfully: alpha\nPreCompact [{CreateEchoCommand(shell.Value, "beta")}] completed successfully: beta",
                result.UserDisplayMessage);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunPostCompactAsync_Aggregates_Success_And_Failure_Display_Messages()
    {
        var shell = await GetAvailableShellAsync();
        if (shell is null)
        {
            return;
        }

        var root = CreateTempRoot();
        try
        {
            var successCommand = CreateEchoCommand(shell.Value, "done");
            var failureCommand = CreateFailureCommand(shell.Value, "boom");
            var runner = CreateRunner(
                root,
                [
                    CreateHook(HookEvent.PostCompact, "manual", shell.Value, successCommand, "settings"),
                    CreateHook(HookEvent.PostCompact, "manual", shell.Value, failureCommand, "plugin")
                ]);

            var result = await runner.RunPostCompactAsync(CreateContext(root), "summary text");

            Assert.Equal(
                $"PostCompact [{successCommand}] completed successfully: done\nPostCompact [{failureCommand}] failed: boom",
                result.UserDisplayMessage);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static QueryReactiveCompactHookRunner CreateRunner(string root, IReadOnlyList<HookDefinition> hooks)
    {
        var taskRegistry = new TaskRegistry(root);
        var appState = ClawSharpAppState.CreateDefault(
            root,
            StartupEnvironment.Capture(),
            new ClawSharpSettings(),
            [],
            [],
            [],
            [],
            [],
            hooks);
        var appStateStore = new ClawSharpAppStateStore(appState);
        var toolRegistry = new ToolRegistry(root, taskRegistry, appStateStore: appStateStore);
        return new QueryReactiveCompactHookRunner(toolRegistry);
    }

    private static QueryReactiveCompactExecutionContext CreateContext(string root)
    {
        var session = new ConversationSession("session-reactive-compact-hooks", root, Path.Combine(root, "session.jsonl"));
        var request = QueryTurnRequest.Create(session, "hello") with
        {
            ModelTurnContext = new QueryModelTurnContext(
                ["system prompt"],
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, string>(StringComparer.Ordinal),
                "user")
        };
        var priorState = QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateUserMessage("hello")]);
        var terminalResult = new QueryTerminalIterationResult(
            new QueryLoopTerminal(QueryTerminalReason.PromptTooLong),
            priorState);
        return new QueryReactiveCompactExecutionContext(
            request,
            priorState,
            terminalResult,
            session,
            new ClawSharpSettings(),
            []);
    }

    private static HookDefinition CreateHook(
        HookEvent hookEvent,
        string matcher,
        HookShell shell,
        string command,
        string source)
    {
        return new HookDefinition(
            hookEvent,
            source,
            matcher,
            new HookCommandDefinition(HookKind.Command, Command: command, Shell: shell, TimeoutSeconds: 5));
    }

    private static string CreateEchoCommand(HookShell shell, string output)
    {
        return shell == HookShell.PowerShell
            ? $"Write-Output '{output}'"
            : $"printf '%s\\n' '{output}'";
    }

    private static string CreateFailureCommand(HookShell shell, string output)
    {
        return shell == HookShell.PowerShell
            ? $"Write-Output '{output}'; exit 1"
            : $"echo '{output}'; exit 1";
    }

    private static async Task<HookShell?> GetAvailableShellAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            var powerShellPath = await PowerShellDetection.GetCachedPowerShellPathAsync();
            return string.IsNullOrWhiteSpace(powerShellPath) ? null : HookShell.PowerShell;
        }

        var bashPath = await BashShellDetection.FindSuitableShellAsync();
        return string.IsNullOrWhiteSpace(bashPath) ? null : HookShell.Bash;
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "clawsharp-reactive-compact-hook-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
