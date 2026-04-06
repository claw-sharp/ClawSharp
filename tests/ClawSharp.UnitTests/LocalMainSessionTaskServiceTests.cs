// TS origin: ./tasks/LocalMainSessionTask.ts, ./hooks/useSessionBackgrounding.ts
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Query;
using ClawSharp.Tasks;
using TaskStatus = ClawSharp.Tasks.TaskStatus;

namespace ClawSharp.UnitTests;

public sealed class LocalMainSessionTaskServiceTests
{
    [Fact]
    public async Task StartBackgroundSessionAsync_Completes_MainSession_Task_And_Queues_Notification()
    {
        var tempDir = CreateTempDirectory();
        var queue = new InMemoryQueuedCommandQueue();
        var transcriptStore = new JsonlTranscriptStore();
        var appStateStore = CreateAppStateStore(tempDir);
        var tasks = new TaskRegistry(tempDir, queuedCommandQueue: queue, appStateStore: appStateStore);
        var queryEngine = CreateQueryEngine(transcriptStore, queue, new SuccessfulBackgroundSessionTurnRunner("Background result"));
        var service = new LocalMainSessionTaskService(tasks, queue, queryEngine, appStateStore, transcriptStore);
        var session = new DefaultSessionFactory(tempDir, transcriptStore).Create();
        session.Add(ChatMessageFactory.CreateText(MessageRole.User, "Previous context"));

        var task = await service.StartBackgroundSessionAsync(
            session,
            QueryTurnRequest.Create(session, "Continue the work") with
            {
                AbortReason = QueryAbortReason.Interrupt
            },
            "Background session");

        var completedTask = await WaitForTaskStatusAsync(tasks, task.Id, TaskStatus.Completed);

        Assert.NotNull(completedTask);
        Assert.StartsWith("s", completedTask!.Id, StringComparison.Ordinal);
        Assert.Equal(LocalMainSessionTaskService.MainSessionAgentType, completedTask.AgentType);
        Assert.True(completedTask.IsBackgrounded);
        Assert.Equal("Background result", completedTask.Result);
        Assert.NotNull(completedTask.Messages);
        Assert.Single(completedTask.Messages!);
        Assert.Equal("Background result", completedTask.Messages![0].Content);
        AssertHasTerminalGraceWindow(completedTask);

        var notification = await WaitForNotificationAsync(queue);
        Assert.NotNull(notification);
        Assert.Contains("<task-notification>", notification, StringComparison.Ordinal);
        Assert.Contains("<summary>Background session \"Background session\" completed</summary>", notification, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForegroundMainSessionTask_Tracks_Foreground_State_And_Backgrounds_Previous_Task()
    {
        var tempDir = CreateTempDirectory();
        var queue = new InMemoryQueuedCommandQueue();
        var transcriptStore = new JsonlTranscriptStore();
        var appStateStore = CreateAppStateStore(tempDir);
        var tasks = new TaskRegistry(tempDir, queuedCommandQueue: queue, appStateStore: appStateStore);
        var queryEngine = CreateQueryEngine(transcriptStore, queue, new SuccessfulBackgroundSessionTurnRunner("unused"));
        var service = new LocalMainSessionTaskService(tasks, queue, queryEngine, appStateStore, transcriptStore);
        var session = new DefaultSessionFactory(tempDir, transcriptStore).Create();
        var firstTask = await tasks.CreateMainSessionTaskForSessionAsync(
            session.Id,
            "First",
            messages: [ChatMessageFactory.CreateText(MessageRole.Assistant, "first")]);
        var secondTask = await tasks.CreateMainSessionTaskForSessionAsync(
            session.Id,
            "Second",
            messages: [ChatMessageFactory.CreateText(MessageRole.Assistant, "second")]);

        var firstMessages = service.ForegroundMainSessionTask(firstTask.Id);
        var secondMessages = service.ForegroundMainSessionTask(secondTask.Id);

        Assert.NotNull(firstMessages);
        Assert.NotNull(secondMessages);
        Assert.Equal("second", secondMessages![0].Content);
        Assert.Equal(secondTask.Id, appStateStore.GetState().ForegroundedTaskId);
        Assert.True(tasks.TryGet(firstTask.Id, out var updatedFirstTask));
        Assert.True(tasks.TryGet(secondTask.Id, out var updatedSecondTask));
        Assert.True(((LocalAgentTask)updatedFirstTask!).IsBackgrounded);
        Assert.False(((LocalAgentTask)updatedSecondTask!).IsBackgrounded);
        Assert.DoesNotContain(
            ClawSharpAppStateSelectors.GetRunningTasks(appStateStore.GetState()),
            task => string.Equals(task.Id, secondTask.Id, StringComparison.Ordinal));

        Assert.True(service.TryBackgroundForegroundedTask());
        Assert.Null(appStateStore.GetState().ForegroundedTaskId);
        Assert.True(tasks.TryGet(secondTask.Id, out var rebackgroundedTask));
        Assert.True(((LocalAgentTask)rebackgroundedTask!).IsBackgrounded);
    }

    private static QueryEngine CreateQueryEngine(
        ITranscriptStore transcriptStore,
        IQueuedCommandQueue queue,
        IQueryTurnRunner queryTurnRunner)
    {
        return new QueryEngine(
            new ClawSharpSettings(),
            new InMemoryEventSink(),
            transcriptStore,
            queryTurnRunner,
            new QueuedTaskNotificationDrainer(queue, transcriptStore));
    }

    private static async Task<LocalAgentTask?> WaitForTaskStatusAsync(TaskRegistry tasks, string taskId, TaskStatus status)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            if (tasks.TryGet(taskId, out var task) && task is LocalAgentTask localAgentTask && localAgentTask.Status == status)
            {
                return localAgentTask;
            }

            await Task.Delay(25);
        }

        return null;
    }

    private static async Task<string?> WaitForNotificationAsync(InMemoryQueuedCommandQueue queue)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var notification = queue.Dequeue();
            if (notification is not null)
            {
                return notification.Value;
            }

            await Task.Delay(20);
        }

        return null;
    }

    private static void AssertHasTerminalGraceWindow(LocalAgentTask task)
    {
        Assert.NotNull(task.EndTime);
        Assert.NotNull(task.EvictAfter);
        Assert.True(task.EvictAfter > task.EndTime);
    }

    private static ClawSharpAppStateStore CreateAppStateStore(string workspaceRoot)
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
                BuiltInAgentDefinitions.GetBuiltInAgents(),
                []));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "clawsharp-main-session-task-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class SuccessfulBackgroundSessionTurnRunner(string resultText) : IQueryTurnRunner
    {
        public async IAsyncEnumerable<QueryRuntimeEvent> RunAsync(
            QueryTurnRequest request,
            ConversationSession session,
            ClawSharpSettings settings,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new QueryRequestStartRuntimeEvent();
            yield return new QueryMessageRuntimeEvent(
                ChatMessageFactory.CreateText(MessageRole.Assistant, resultText));
            yield return new QueryLoopTerminalRuntimeEvent(
                new QueryLoopTerminal(QueryTerminalReason.Completed),
                QueryLoopStateFactory.CreateInitial(session.Messages.ToArray()));
            await Task.CompletedTask;
        }
    }
}
