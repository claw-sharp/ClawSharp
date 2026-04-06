using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Query;
using ClawSharp.Tasks;
using ClawSharp.Tools;
using ClawSharp.Ui.Terminal;

namespace ClawSharp.UnitTests;

public sealed class TerminalShellPreventSleepTests
{
    [Fact]
    public async Task RunReplAsync_Wraps_Foreground_Turn_With_PreventSleep_Service()
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
            new CompletedTurnRunner("Foreground result"),
            new QueuedTaskNotificationDrainer(queue, transcriptStore, tasks),
            toolRegistry: tools);
        var preventSleepService = new RecordingPreventSleepService();
        var shell = new TerminalShell(
            queryEngine,
            new QueuedTaskNotificationDrainer(queue, transcriptStore, tasks),
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
            preventSleepService: preventSleepService);
        var session = new DefaultSessionFactory(tempDir, transcriptStore).Create();
        using var input = new StringReader("Explain repo\n/exit\n");
        using var output = new StringWriter();

        var exitCode = await shell.RunReplAsync(input, output, session);

        Assert.Equal(0, exitCode);
        Assert.Equal(1, preventSleepService.StartCalls);
        Assert.Equal(1, preventSleepService.StopCalls);
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
        var path = Path.Combine(Path.GetTempPath(), "clawsharp-terminal-prevent-sleep-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class RecordingPreventSleepService : IPreventSleepService
    {
        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public int ForceStopCalls { get; private set; }

        public void StartPreventSleep()
        {
            StartCalls++;
        }

        public void StopPreventSleep()
        {
            StopCalls++;
        }

        public void ForceStopPreventSleep()
        {
            ForceStopCalls++;
        }
    }

    private sealed class CompletedTurnRunner(string assistantText) : IQueryTurnRunner
    {
        public IAsyncEnumerable<QueryRuntimeEvent> RunAsync(
            QueryTurnRequest request,
            ConversationSession session,
            ClawSharpSettings settings,
            CancellationToken cancellationToken = default)
        {
            return RunCore(session);
        }

        async IAsyncEnumerable<QueryRuntimeEvent> RunCore(
            ConversationSession session,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new QueryRequestStartRuntimeEvent();
            yield return new QueryMessageRuntimeEvent(ChatMessageFactory.CreateText(MessageRole.Assistant, assistantText));
            yield return new QueryLoopTerminalRuntimeEvent(
                new QueryLoopTerminal(QueryTerminalReason.Completed),
                QueryLoopStateFactory.CreateInitial(session.Messages.ToArray()));
            await Task.CompletedTask.ConfigureAwait(false);
        }
    }
}
