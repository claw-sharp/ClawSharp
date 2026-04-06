using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Query;
using ClawSharp.Tasks;
using ClawSharp.Tools;
using ClawSharp.Ui.Terminal;

namespace ClawSharp.UnitTests;

public sealed class TerminalShellToolUseRenderingTests
{
    [Fact]
    public async Task RunReplAsync_Prints_Tool_Use_Summary_For_Grep_Turns()
    {
        var tempDir = CreateTempDirectory();
        var settings = new ClawSharpSettings();
        var transcriptStore = new JsonlTranscriptStore();
        var queue = new InMemoryQueuedCommandQueue();
        var appStateStore = CreateAppStateStore(tempDir, settings);
        var tasks = new TaskRegistry(tempDir, queuedCommandQueue: queue, appStateStore: appStateStore);
        var tools = new ToolRegistry(tempDir, tasks, appStateStore: appStateStore);
        var queryEngine = new QueryEngine(
            settings,
            new InMemoryEventSink(),
            transcriptStore,
            new ToolUseThenAnswerTurnRunner(),
            new QueuedTaskNotificationDrainer(queue, transcriptStore, tasks),
            toolRegistry: tools);
        var shell = new TerminalShell(
            queryEngine,
            new QueuedTaskNotificationDrainer(queue, transcriptStore, tasks),
            new ToolUseMessageRenderer(tools),
            new ToolProgressMessageRenderer(tools, new TerminalProgressIndicatorRenderer()),
            new ToolResultMessageRenderer(tools),
            new CommandRegistry(),
            settings,
            new InMemoryEventSink(),
            new DefaultSessionFactory(tempDir, transcriptStore),
            transcriptStore,
            appStateStore: appStateStore,
            readFileState: tools.ReadFileState,
            toolRegistry: tools,
            preventSleepService: new NoOpPreventSleepService());
        var session = new DefaultSessionFactory(tempDir, transcriptStore).Create();
        using var input = new StringReader("Find TODOs\n/exit\n");
        using var output = new StringWriter();

        var exitCode = await shell.RunReplAsync(input, output, session);
        var transcript = output.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("* Grep (pattern: \"TODO\", path: \"src\")", transcript, StringComparison.Ordinal);
        Assert.Contains("* Search completed.", transcript, StringComparison.Ordinal);
    }

    private static ClawSharpAppStateStore CreateAppStateStore(string workspaceRoot, ClawSharpSettings settings)
    {
        return new ClawSharpAppStateStore(
            ClawSharpAppState.CreateDefault(
                workspaceRoot,
                StartupEnvironment.Capture(),
                settings,
                [],
                [],
                [],
                [],
                BuiltInAgentDefinitions.GetBuiltInAgents(),
                []));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "clawsharp-terminal-tool-use-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class NoOpPreventSleepService : IPreventSleepService
    {
        public void ForceStopPreventSleep()
        {
        }

        public void StartPreventSleep()
        {
        }

        public void StopPreventSleep()
        {
        }
    }

    private sealed class ToolUseThenAnswerTurnRunner : IQueryTurnRunner
    {
        public async IAsyncEnumerable<QueryRuntimeEvent> RunAsync(
            QueryTurnRequest request,
            ConversationSession session,
            ClawSharpSettings settings,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new QueryRequestStartRuntimeEvent();
            yield return new QueryMessageRuntimeEvent(
                ChatMessageFactory.CreateToolUse(
                [
                    ("tooluse-grep-1", "Grep", """{"pattern":"TODO","path":"src"}""")
                ]));
            yield return new QueryMessageRuntimeEvent(
                ChatMessageFactory.CreateText(MessageRole.Assistant, "Search completed."));
            yield return new QueryLoopTerminalRuntimeEvent(
                new QueryLoopTerminal(QueryTerminalReason.Completed),
                QueryLoopStateFactory.CreateInitial(session.Messages.ToArray()));
            await Task.CompletedTask;
        }
    }
}
