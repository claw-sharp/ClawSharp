// TS origin: ./tools/AgentTool/AgentTool.tsx, ./tools/AgentTool/runAgent.ts
using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Query;
using ClawSharp.Tasks;
using ClawSharp.Tools;
using TaskStatus = ClawSharp.Tasks.TaskStatus;

namespace ClawSharp.UnitTests;

public sealed class LocalAgentExecutionServiceTests
{
    [Fact]
    public async Task AgentTool_Executes_Foreground_Local_Agent_And_Filters_Child_Tools()
    {
        var tempDir = CreateTempDirectory();
        using var configDir = new EnvironmentVariableScope("CLAUDE_CONFIG_DIR", Path.Combine(tempDir, ".claude-config"));
        var queue = new InMemoryQueuedCommandQueue();
        var transcriptStore = new JsonlTranscriptStore();
        var appStateStore = CreateAppStateStore(tempDir);
        var tasks = new TaskRegistry(tempDir, queuedCommandQueue: queue, appStateStore: appStateStore);
        IReadOnlyList<string>? childToolNames = null;
        var service = new LocalAgentExecutionService(
            new InMemoryEventSink(),
            transcriptStore,
            new AgentPersistenceService(transcriptStore),
            queue,
            new NotImplementedQueryModelCallExecutor(),
            queryEngineFactory: (context, childTools) =>
            {
                childToolNames = childTools.All.Select(static tool => tool.Name).ToArray();
                return CreateQueryEngine(context.Settings, transcriptStore, queue, new SuccessfulAgentQueryTurnRunner("Foreground result"));
            });
        var registry = new ToolRegistry(tempDir, tasks, appStateStore: appStateStore, agentExecutionService: service);
        var session = new DefaultSessionFactory(tempDir).Create();

        var result = await registry.ExecuteAsync(
            "Agent",
            """{"description":"inspect repo","prompt":"inspect the repo","subagent_type":"Explore"}""",
            session,
            new ClawSharpSettings());

        Assert.True(result.Success);
        Assert.Equal("completed", result.StructuredOutput?["status"]?.GetValue<string>());
        Assert.Equal("Foreground result", result.StructuredOutput?["result"]?.GetValue<string>());
        Assert.NotNull(childToolNames);
        Assert.Contains("Read", childToolNames!);
        Assert.Contains("Glob", childToolNames);
        Assert.DoesNotContain("Edit", childToolNames);
        Assert.DoesNotContain("Agent", childToolNames);
        Assert.DoesNotContain("SendMessage", childToolNames);

        Assert.Single(tasks.GetAll());
        var task = Assert.IsType<LocalAgentTask>(tasks.GetAll().Single());
        Assert.Equal(TaskStatus.Completed, task.Status);
        Assert.False(task.IsBackgrounded);
        Assert.Equal("Foreground result", task.Result);
        AssertHasTerminalGraceWindow(task);
    }

    [Fact]
    public async Task AgentTool_Executes_Background_Local_Agent_And_Queues_Notification()
    {
        var tempDir = CreateTempDirectory();
        using var configDir = new EnvironmentVariableScope("CLAUDE_CONFIG_DIR", Path.Combine(tempDir, ".claude-config"));
        var queue = new InMemoryQueuedCommandQueue();
        var transcriptStore = new JsonlTranscriptStore();
        var appStateStore = CreateAppStateStore(tempDir);
        var tasks = new TaskRegistry(tempDir, queuedCommandQueue: queue, appStateStore: appStateStore);
        var service = new LocalAgentExecutionService(
            new InMemoryEventSink(),
            transcriptStore,
            new AgentPersistenceService(transcriptStore),
            queue,
            new NotImplementedQueryModelCallExecutor(),
            queryEngineFactory: (context, childTools) =>
                CreateQueryEngine(context.Settings, transcriptStore, queue, new SuccessfulAgentQueryTurnRunner("Background result")));
        var registry = new ToolRegistry(tempDir, tasks, appStateStore: appStateStore, agentExecutionService: service);
        var session = new DefaultSessionFactory(tempDir).Create();

        var result = await registry.ExecuteAsync(
            "Agent",
            """{"description":"inspect repo","prompt":"inspect the repo","run_in_background":true}""",
            session,
            new ClawSharpSettings());

        Assert.True(result.Success);
        Assert.Equal("async_launched", result.StructuredOutput?["status"]?.GetValue<string>());

        var completedTask = await WaitForAgentCompletionAsync(tasks);
        Assert.NotNull(completedTask);
        Assert.Equal(TaskStatus.Completed, completedTask!.Status);
        Assert.True(completedTask.IsBackgrounded);
        Assert.Equal("Background result", completedTask.Result);
        AssertHasTerminalGraceWindow(completedTask);

        var notification = await WaitForNotificationAsync(queue);
        Assert.NotNull(notification);
        Assert.Contains("<summary>Agent \"inspect repo\" completed</summary>", notification!, StringComparison.Ordinal);
        Assert.Contains("<result>Background result</result>", notification, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TaskStopTool_Stops_Background_Local_Agent_And_Queues_Stopped_Notification()
    {
        var tempDir = CreateTempDirectory();
        using var configDir = new EnvironmentVariableScope("CLAUDE_CONFIG_DIR", Path.Combine(tempDir, ".claude-config"));
        var queue = new InMemoryQueuedCommandQueue();
        var transcriptStore = new JsonlTranscriptStore();
        var appStateStore = CreateAppStateStore(tempDir);
        var tasks = new TaskRegistry(tempDir, queuedCommandQueue: queue, appStateStore: appStateStore);
        var service = new LocalAgentExecutionService(
            new InMemoryEventSink(),
            transcriptStore,
            new AgentPersistenceService(transcriptStore),
            queue,
            new NotImplementedQueryModelCallExecutor(),
            queryEngineFactory: (context, childTools) =>
                CreateQueryEngine(context.Settings, transcriptStore, queue, new BlockingAgentQueryTurnRunner()));
        var registry = new ToolRegistry(tempDir, tasks, appStateStore: appStateStore, agentExecutionService: service);
        var session = new DefaultSessionFactory(tempDir).Create();

        var result = await registry.ExecuteAsync(
            "Agent",
            """{"description":"inspect repo","prompt":"inspect the repo","run_in_background":true}""",
            session,
            new ClawSharpSettings());

        Assert.True(result.Success);
        var taskId = result.StructuredOutput?["agentId"]?.GetValue<string>();
        Assert.False(string.IsNullOrWhiteSpace(taskId));

        var stopResult = await registry.ExecuteAsync(
            "TaskStop",
            $$"""{"task_id":"{{taskId}}"}""",
            session,
            new ClawSharpSettings());

        Assert.True(stopResult.Success, stopResult.Output);
        var killedTask = await WaitForTaskStatusAsync(tasks, taskId!, TaskStatus.Killed);
        Assert.NotNull(killedTask);
        AssertHasTerminalGraceWindow(killedTask!);

        var notification = await WaitForNotificationAsync(queue);
        Assert.NotNull(notification);
        Assert.Contains("<summary>Agent \"inspect repo\" was stopped</summary>", notification!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentTool_WithForkGate_Omitted_SubagentType_Uses_Fork_Runtime_And_Inherits_Parent_Context()
    {
        var tempDir = CreateTempDirectory();
        using var configDir = new EnvironmentVariableScope("CLAUDE_CONFIG_DIR", Path.Combine(tempDir, ".claude-config"));
        using var forkGate = new EnvironmentVariableScope(ForkSubagentFoundation.ForkSubagentEnvironmentVariable, "1");
        var queue = new InMemoryQueuedCommandQueue();
        var transcriptStore = new JsonlTranscriptStore();
        var appStateStore = CreateAppStateStore(tempDir);
        var tasks = new TaskRegistry(tempDir, queuedCommandQueue: queue, appStateStore: appStateStore);
        QueryTurnRequest? capturedRequest = null;
        ChatMessage[]? capturedSeedMessages = null;
        IReadOnlyList<string>? childToolNames = null;
        var service = new LocalAgentExecutionService(
            new InMemoryEventSink(),
            transcriptStore,
            new AgentPersistenceService(transcriptStore),
            queue,
            new NotImplementedQueryModelCallExecutor(),
            queryEngineFactory: (context, childTools) =>
            {
                childToolNames = childTools.All.Select(static tool => tool.Name).ToArray();
                return CreateQueryEngine(
                    context.Settings,
                    transcriptStore,
                    queue,
                    new CapturingAgentQueryTurnRunner(
                        "Fork result",
                        (request, session) =>
                        {
                            capturedRequest = request;
                            capturedSeedMessages = session.Messages.ToArray();
                        }));
            });
        var registry = new ToolRegistry(tempDir, tasks, appStateStore: appStateStore, agentExecutionService: service);
        var session = new DefaultSessionFactory(tempDir).Create();
        session.Add(ChatMessageFactory.CreateText(MessageRole.User, "Parent request"));
        session.Add(
            ChatMessageFactory.CreateToolUse(
                [("tooluse-read", "Read", """{"path":"README.md"}""")]));
        registry.ReadFileState.Set(
            Path.Combine(tempDir, "README.md"),
            new FileState("cached read", 1234, Offset: null, Limit: null));

        Assert.True(registry.TryResolve("Agent", out var agentTool));

        var result = await agentTool!.ExecuteAsync(
            CreateToolExecutionContext(
                registry,
                session,
                """{"description":"fork inspect","prompt":"inspect the repo"}""",
                querySource: QueryModelTurnContext.ReplMainThread.QuerySource,
                currentSystemPrompt:
                [
                    "parent system prompt",
                    "secondary parent prompt"
                ]));

        Assert.True(result.Success);
        Assert.Equal("async_launched", result.StructuredOutput?["status"]?.GetValue<string>());

        var completedTask = await WaitForAgentCompletionAsync(tasks);
        Assert.NotNull(completedTask);
        Assert.Equal(TaskStatus.Completed, completedTask!.Status);
        Assert.True(completedTask.IsBackgrounded);
        Assert.Equal(ForkSubagentFoundation.ForkSubagentType, completedTask.AgentType);
        Assert.Equal("Fork result", completedTask.Result);

        Assert.NotNull(capturedRequest);
        Assert.Equal($"agent:builtin:{ForkSubagentFoundation.ForkSubagentType}", capturedRequest!.ModelTurnContext?.QuerySource);
        Assert.Equal(["parent system prompt", "secondary parent prompt"], capturedRequest.ModelTurnContext?.SystemPrompt);
        Assert.Equal("cached read", capturedRequest.InitialToolUseContext?.ReadFileState.Get(Path.Combine(tempDir, "README.md"))?.Content);
        Assert.NotSame(registry.ReadFileState, capturedRequest.InitialToolUseContext?.ReadFileState);

        Assert.NotNull(capturedSeedMessages);
        Assert.Equal(3, capturedSeedMessages!.Length);
        Assert.Equal(MessageRole.User, capturedSeedMessages[0].Role);
        Assert.Equal("Parent request", capturedSeedMessages[0].ContentBlocks[0].Value);
        Assert.Equal(MessageRole.Assistant, capturedSeedMessages[1].Role);
        Assert.Equal(MessageRole.User, capturedSeedMessages[2].Role);
        Assert.Equal(MessageContentKind.ToolResult, capturedSeedMessages[2].ContentBlocks[0].Kind);
        Assert.Contains("STOP. READ THIS FIRST.", capturedSeedMessages[2].ContentBlocks[^1].Value);

        Assert.NotNull(childToolNames);
        Assert.Equal(
            registry.All.Select(static tool => tool.Name).OrderBy(static name => name, StringComparer.OrdinalIgnoreCase),
            childToolNames!.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AgentTool_WithForkGate_Blocks_Recursive_Fork_In_Fork_Child()
    {
        var tempDir = CreateTempDirectory();
        using var configDir = new EnvironmentVariableScope("CLAUDE_CONFIG_DIR", Path.Combine(tempDir, ".claude-config"));
        using var forkGate = new EnvironmentVariableScope(ForkSubagentFoundation.ForkSubagentEnvironmentVariable, "1");
        var appStateStore = CreateAppStateStore(tempDir);
        var tasks = new TaskRegistry(tempDir, appStateStore: appStateStore);
        var registry = new ToolRegistry(tempDir, tasks, appStateStore: appStateStore);
        var session = new DefaultSessionFactory(tempDir).Create();
        session.Add(ChatMessageFactory.CreateText(MessageRole.User, ForkSubagentFoundation.BuildChildMessage("nested")));

        Assert.True(registry.TryResolve("Agent", out var agentTool));

        var validation = await agentTool!.ValidateAsync(
            CreateToolExecutionContext(
                registry,
                session,
                """{"description":"fork inspect","prompt":"inspect the repo"}""",
                querySource: $"agent:builtin:{ForkSubagentFoundation.ForkSubagentType}",
                currentSystemPrompt: ["parent system prompt"]));

        Assert.False(validation.IsValid);
        Assert.Contains("Fork is not available inside a forked worker", validation.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentTool_WithForkGate_Forces_Background_Execution_For_Explicit_Subagent()
    {
        var tempDir = CreateTempDirectory();
        using var configDir = new EnvironmentVariableScope("CLAUDE_CONFIG_DIR", Path.Combine(tempDir, ".claude-config"));
        using var forkGate = new EnvironmentVariableScope(ForkSubagentFoundation.ForkSubagentEnvironmentVariable, "1");
        var queue = new InMemoryQueuedCommandQueue();
        var transcriptStore = new JsonlTranscriptStore();
        var appStateStore = CreateAppStateStore(tempDir);
        var tasks = new TaskRegistry(tempDir, queuedCommandQueue: queue, appStateStore: appStateStore);
        var service = new LocalAgentExecutionService(
            new InMemoryEventSink(),
            transcriptStore,
            new AgentPersistenceService(transcriptStore),
            queue,
            new NotImplementedQueryModelCallExecutor(),
            queryEngineFactory: (context, childTools) =>
                CreateQueryEngine(context.Settings, transcriptStore, queue, new SuccessfulAgentQueryTurnRunner("Explore result")));
        var registry = new ToolRegistry(tempDir, tasks, appStateStore: appStateStore, agentExecutionService: service);
        var session = new DefaultSessionFactory(tempDir).Create();

        var result = await registry.ExecuteAsync(
            "Agent",
            """{"description":"inspect repo","prompt":"inspect the repo","subagent_type":"Explore"}""",
            session,
            new ClawSharpSettings());

        Assert.True(result.Success);
        Assert.Equal("async_launched", result.StructuredOutput?["status"]?.GetValue<string>());

        var completedTask = await WaitForAgentCompletionAsync(tasks);
        Assert.NotNull(completedTask);
        Assert.True(completedTask!.IsBackgrounded);
        Assert.Equal("Explore", completedTask.AgentType);
    }

    private static QueryEngine CreateQueryEngine(
        ClawSharpSettings settings,
        ITranscriptStore transcriptStore,
        IQueuedCommandQueue queue,
        IQueryTurnRunner queryTurnRunner)
    {
        return new QueryEngine(
            settings,
            new InMemoryEventSink(),
            transcriptStore,
            queryTurnRunner,
            new QueuedTaskNotificationDrainer(queue, transcriptStore));
    }

    private static async Task<LocalAgentTask?> WaitForAgentCompletionAsync(TaskRegistry tasks)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var task = tasks.GetAll().SingleOrDefault() as LocalAgentTask;
            if (task is not null && task.Status.IsTerminal())
            {
                return task;
            }

            await Task.Delay(20);
        }

        return tasks.GetAll().SingleOrDefault() as LocalAgentTask;
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
        Assert.InRange(
            (task.EvictAfter!.Value - task.EndTime!.Value).TotalSeconds,
            29,
            31);
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

    private static ToolExecutionContext CreateToolExecutionContext(
        ToolRegistry registry,
        ConversationSession session,
        string arguments,
        string? querySource = null,
        IReadOnlyList<string>? currentSystemPrompt = null)
    {
        return new ToolExecutionContext(
            arguments,
            registry.WorkspaceRoot,
            session,
            registry.AppStateStore,
            registry.Tasks,
            registry.Tasks,
            new ClawSharpSettings(),
            registry.ReadFileState,
            registry.ToolPermissionContext,
            registry.AgentDefinitions,
            registry.FileUpdateNotifier,
            registry.PermissionPrompter,
            QuerySource: querySource,
            CurrentSystemPrompt: currentSystemPrompt,
            AvailableTools: registry.All);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "clawsharp-local-agent-execution-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class SuccessfulAgentQueryTurnRunner(string resultText) : IQueryTurnRunner
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
                    [("tooluse-read", "Read", """{"path":"README.md"}""")]));
            yield return new QueryMessageRuntimeEvent(
                ChatMessageFactory.CreateToolResult("tooluse-read", "Read", "README"));
            yield return new QueryMessageRuntimeEvent(
                ChatMessageFactory.CreateText(MessageRole.Assistant, resultText));
            yield return new QueryLoopTerminalRuntimeEvent(
                new QueryLoopTerminal(QueryTerminalReason.Completed),
                QueryLoopStateFactory.CreateInitial(session.Messages.ToArray()));
            await Task.CompletedTask;
        }
    }

    private sealed class CapturingAgentQueryTurnRunner(
        string resultText,
        Action<QueryTurnRequest, ConversationSession> capture) : IQueryTurnRunner
    {
        public async IAsyncEnumerable<QueryRuntimeEvent> RunAsync(
            QueryTurnRequest request,
            ConversationSession session,
            ClawSharpSettings settings,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            capture(request, session);
            yield return new QueryRequestStartRuntimeEvent();
            yield return new QueryMessageRuntimeEvent(
                ChatMessageFactory.CreateText(MessageRole.Assistant, resultText));
            yield return new QueryLoopTerminalRuntimeEvent(
                new QueryLoopTerminal(QueryTerminalReason.Completed),
                QueryLoopStateFactory.CreateInitial(session.Messages.ToArray()));
            await Task.CompletedTask;
        }
    }

    private sealed class BlockingAgentQueryTurnRunner : IQueryTurnRunner
    {
        public async IAsyncEnumerable<QueryRuntimeEvent> RunAsync(
            QueryTurnRequest request,
            ConversationSession session,
            ClawSharpSettings settings,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new QueryRequestStartRuntimeEvent();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _previousValue);
        }
    }
}
