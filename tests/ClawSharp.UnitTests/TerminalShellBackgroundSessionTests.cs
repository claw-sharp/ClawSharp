using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Query;
using ClawSharp.Tasks;
using ClawSharp.Tools;
using ClawSharp.Ui.Terminal;
using TaskStatus = ClawSharp.Tasks.TaskStatus;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public sealed class TerminalShellBackgroundSessionTests
{
    [Fact]
    public async Task RunReplAsync_Backgrounds_Interactive_Query_Into_MainSession_Task()
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
            new ForegroundThenBackgroundTurnRunner("Background result"),
            new QueuedTaskNotificationDrainer(queue, transcriptStore, tasks),
            toolRegistry: tools);
        var service = new LocalMainSessionTaskService(tasks, queue, queryEngine, appStateStore, transcriptStore);
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
            localMainSessionTaskService: service,
            sessionBackgroundKeyMonitor: new TriggeredBackgroundKeyMonitor());
        var session = new DefaultSessionFactory(tempDir, transcriptStore).Create();
        using var input = new StringReader("Investigate repo\n");
        using var output = new StringWriter();

        var exitCode = await shell.RunReplAsync(input, output, session);
        var backgroundTask = await WaitForCompletedMainSessionTaskAsync(tasks);

        Assert.Equal(0, exitCode);
        Assert.NotNull(backgroundTask);
        Assert.Equal(LocalMainSessionTaskService.MainSessionAgentType, backgroundTask!.AgentType);
        Assert.True(backgroundTask.IsBackgrounded);
        Assert.Equal(TaskStatus.Completed, backgroundTask.Status);
        Assert.Contains("Backgrounded session as task", output.ToString(), StringComparison.Ordinal);

        var notification = await WaitForNotificationAsync(queue);
        Assert.NotNull(notification);
        Assert.Contains("<task-notification>", notification, StringComparison.Ordinal);
    }

    private static async Task<LocalAgentTask?> WaitForCompletedMainSessionTaskAsync(TaskRegistry tasks)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            var task = tasks.GetAll().OfType<LocalAgentTask>().SingleOrDefault();
            if (task is not null && task.Status == TaskStatus.Completed)
            {
                return task;
            }

            await Task.Delay(25);
        }

        return tasks.GetAll().OfType<LocalAgentTask>().SingleOrDefault();
    }

    private static async Task<string?> WaitForNotificationAsync(InMemoryQueuedCommandQueue queue)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            var notification = queue.Dequeue();
            if (notification is not null)
            {
                return notification.Value;
            }

            await Task.Delay(25);
        }

        return null;
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
        var path = Path.Combine(Path.GetTempPath(), "clawsharp-terminal-background-session-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TriggeredBackgroundKeyMonitor : ISessionBackgroundKeyMonitor
    {
        public bool CanMonitor(TextReader input)
        {
            return true;
        }

        public async Task<bool> WaitForBackgroundRequestAsync(
            TextReader input,
            TextWriter output,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(50, cancellationToken);
            return true;
        }
    }

    private sealed class ForegroundThenBackgroundTurnRunner(string backgroundResult) : IQueryTurnRunner
    {
        public async IAsyncEnumerable<QueryRuntimeEvent> RunAsync(
            QueryTurnRequest request,
            ConversationSession session,
            ClawSharpSettings settings,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new QueryRequestStartRuntimeEvent();

            if (request.SessionId.StartsWith("s", StringComparison.Ordinal))
            {
                yield return new QueryMessageRuntimeEvent(
                    ChatMessageFactory.CreateText(MessageRole.Assistant, backgroundResult));
                yield return new QueryLoopTerminalRuntimeEvent(
                    new QueryLoopTerminal(QueryTerminalReason.Completed),
                    QueryLoopStateFactory.CreateInitial(session.Messages.ToArray()));
                yield break;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }
}
