// TS parity status: focused C# coverage for the outer query-loop coordinator structure; real multi-iteration behavior still depends on continuation-producing model-backed iterations.
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Query;
using ClawSharp.Tasks;
using ClawSharp.Tools;

namespace ClawSharp.UnitTests;

public sealed class QueryLoopRunnerTests
{
    [Fact]
    public async Task RunAsync_Emits_Terminal_Event_From_Iteration_Result()
    {
        var runner = new QueryLoopRunner(new SingleTerminalIterationRunner());
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-query-loop-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        var session = new ConversationSession("session-1", workspaceRoot, Path.Combine(workspaceRoot, "session-1.jsonl"));

        var events = new List<QueryRuntimeEvent>();
        await foreach (var runtimeEvent in runner.RunAsync(
            new QueryTurnRequest("session-1", "hello", DateTimeOffset.UtcNow, []),
            session,
            new ClawSharpSettings(),
            CancellationToken.None))
        {
            events.Add(runtimeEvent);
        }

        Assert.Collection(
            events,
            queryEvent => Assert.IsType<QueryRequestStartRuntimeEvent>(queryEvent),
            queryEvent => Assert.IsType<QueryMessageRuntimeEvent>(queryEvent),
            queryEvent =>
            {
                var terminalEvent = Assert.IsType<QueryLoopTerminalRuntimeEvent>(queryEvent);
                Assert.Equal(QueryTerminalReason.Completed, terminalEvent.Terminal.Reason);
            });
    }

    [Fact]
    public async Task RunAsync_Repeats_When_Iteration_Returns_Continue_Result()
    {
        var runner = new QueryLoopRunner(new ContinueThenTerminalIterationRunner());
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-query-loop-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        var session = new ConversationSession("session-2", workspaceRoot, Path.Combine(workspaceRoot, "session-2.jsonl"));

        var events = new List<QueryRuntimeEvent>();
        await foreach (var runtimeEvent in runner.RunAsync(
            new QueryTurnRequest("session-2", "hello", DateTimeOffset.UtcNow, []),
            session,
            new ClawSharpSettings(),
            CancellationToken.None))
        {
            events.Add(runtimeEvent);
        }

        Assert.Equal(6, events.Count);
        Assert.IsType<QueryRequestStartRuntimeEvent>(events[0]);
        Assert.IsType<QueryMessageRuntimeEvent>(events[1]);
        var transitionEvent = Assert.IsType<QueryLoopTransitionRuntimeEvent>(events[2]);
        Assert.Equal(QueryContinueReason.NextTurn, transitionEvent.Transition.Reason);
        Assert.IsType<QueryRequestStartRuntimeEvent>(events[3]);
        Assert.IsType<QueryMessageRuntimeEvent>(events[4]);
        Assert.IsType<QueryLoopTerminalRuntimeEvent>(events[5]);
    }

    [Fact]
    public async Task RunAsync_Stops_At_Max_Turns_Before_Continuing()
    {
        var runner = new QueryLoopRunner(new ContinueThenTerminalIterationRunner());
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-query-loop-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        var session = new ConversationSession("session-3", workspaceRoot, Path.Combine(workspaceRoot, "session-3.jsonl"));

        var events = new List<QueryRuntimeEvent>();
        await foreach (var runtimeEvent in runner.RunAsync(
            new QueryTurnRequest("session-3", "hello", DateTimeOffset.UtcNow, [], MaxTurns: 1),
            session,
            new ClawSharpSettings(),
            CancellationToken.None))
        {
            events.Add(runtimeEvent);
        }

        Assert.Equal(4, events.Count);
        Assert.IsType<QueryRequestStartRuntimeEvent>(events[0]);
        Assert.IsType<QueryMessageRuntimeEvent>(events[1]);

        var maxTurnsMessageEvent = Assert.IsType<QueryMessageRuntimeEvent>(events[2]);
        Assert.True(QueryAttachmentHelpers.TryGetMaxTurnsReachedNotification(maxTurnsMessageEvent.Message, out var notification));
        Assert.NotNull(notification);
        Assert.Equal(1, notification!.MaxTurns);
        Assert.Equal(2, notification.TurnCount);

        var terminalEvent = Assert.IsType<QueryLoopTerminalRuntimeEvent>(events[3]);
        Assert.Equal(QueryTerminalReason.MaxTurns, terminalEvent.Terminal.Reason);
        Assert.Equal(2, terminalEvent.Terminal.TurnCount);
        Assert.True(terminalEvent.State.ToolUseContext.ReadFileState is not null);
    }

    [Fact]
    public async Task RunAsync_Routes_Continuation_From_ExplicitTool_To_ModelBacked_Iteration()
    {
        var runner = new QueryLoopRunner(
            new DispatchingQueryIterationRunner(
                new ContinueToModelBackedIterationRunner(),
                new TerminalModelBackedIterationRunner()));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-query-loop-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        var session = new ConversationSession("session-4", workspaceRoot, Path.Combine(workspaceRoot, "session-4.jsonl"));

        var events = new List<QueryRuntimeEvent>();
        await foreach (var runtimeEvent in runner.RunAsync(
            new QueryTurnRequest(
                "session-4",
                "hello",
                DateTimeOffset.UtcNow,
                [new ToolCallRequest("tooluse-1", "Read", "note.txt")],
                ExecutionMode: QueryTurnExecutionMode.ExplicitTool),
            session,
            new ClawSharpSettings(),
            CancellationToken.None))
        {
            events.Add(runtimeEvent);
        }

        Assert.Equal(6, events.Count);
        Assert.IsType<QueryRequestStartRuntimeEvent>(events[0]);
        Assert.IsType<QueryMessageRuntimeEvent>(events[1]);
        var transitionEvent = Assert.IsType<QueryLoopTransitionRuntimeEvent>(events[2]);
        Assert.Equal(QueryContinueReason.StopHookBlocking, transitionEvent.Transition.Reason);
        Assert.IsType<QueryRequestStartRuntimeEvent>(events[3]);
        var modelMessageEvent = Assert.IsType<QueryMessageRuntimeEvent>(events[4]);
        Assert.Equal("model-iteration", modelMessageEvent.Message.Content);
        var terminalEvent = Assert.IsType<QueryLoopTerminalRuntimeEvent>(events[5]);
        Assert.Equal(QueryTerminalReason.Completed, terminalEvent.Terminal.Reason);
    }

    [Fact]
    public async Task RunAsync_Routes_StopHookBlocking_Continuation_From_ExplicitTool_Iteration()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-query-loop-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        var taskRegistry = new TaskRegistry();
        var eventSink = new InMemoryEventSink();
        var toolRegistry = new ToolRegistry(workspaceRoot, taskRegistry);
        toolRegistry.RegisterOrReplace(new QueryLoopRunnerReadTool());
        var orchestrator = new ToolOrchestrator(toolRegistry, eventSink);
        var explicitToolIterationRunner = new ExplicitToolIterationRunner(
            orchestrator,
            new BlockingStopHookRunner());
        var runner = new QueryLoopRunner(
            new DispatchingQueryIterationRunner(
                explicitToolIterationRunner,
                new TerminalModelBackedIterationRunner()));
        var session = new ConversationSession("session-5", workspaceRoot, Path.Combine(workspaceRoot, "session-5.jsonl"));

        var events = new List<QueryRuntimeEvent>();
        await foreach (var runtimeEvent in runner.RunAsync(
            new QueryTurnRequest(
                "session-5",
                "hello",
                DateTimeOffset.UtcNow,
                [new ToolCallRequest("tooluse-1", "LoopRead", "payload")],
                ExecutionMode: QueryTurnExecutionMode.ExplicitTool),
            session,
            new ClawSharpSettings(),
            CancellationToken.None))
        {
            events.Add(runtimeEvent);
        }

        Assert.Equal(8, events.Count);
        Assert.IsType<QueryRequestStartRuntimeEvent>(events[0]);
        Assert.IsType<QueryMessageRuntimeEvent>(events[1]);
        Assert.IsType<QueryMessageRuntimeEvent>(events[2]);
        var blockingMessageEvent = Assert.IsType<QueryMessageRuntimeEvent>(events[3]);
        Assert.Equal("Stop hook feedback:\nblocked by hook", blockingMessageEvent.Message.Content);
        var transitionEvent = Assert.IsType<QueryLoopTransitionRuntimeEvent>(events[4]);
        Assert.Equal(QueryContinueReason.StopHookBlocking, transitionEvent.Transition.Reason);
        Assert.IsType<QueryRequestStartRuntimeEvent>(events[5]);
        var modelMessageEvent = Assert.IsType<QueryMessageRuntimeEvent>(events[6]);
        Assert.Equal("model-iteration", modelMessageEvent.Message.Content);
        var terminalEvent = Assert.IsType<QueryLoopTerminalRuntimeEvent>(events[7]);
        Assert.Equal(QueryTerminalReason.Completed, terminalEvent.Terminal.Reason);
    }

    [Fact]
    public async Task QueryEngine_Allows_NonCompleted_Terminal_Without_Assistant_Message()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-max-turn-result-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var settings = new ClawSharpSettings();
        var eventSink = new InMemoryEventSink();
        var transcriptStore = new JsonlTranscriptStore();
        var queue = new InMemoryQueuedCommandQueue();
        var queryEngine = new QueryEngine(
            settings,
            eventSink,
            transcriptStore,
            new MaxTurnsWithoutAssistantQueryTurnRunner(),
            new QueuedTaskNotificationDrainer(queue, transcriptStore));
        var session = new DefaultSessionFactory(tempDir).Create();
        var request = QueryTurnRequest.Create(
            session,
            "Read note.txt",
            [
                new ToolCallRequest("tooluse-note", "Read", "note.txt")
            ],
            maxTurns: 1);

        var result = await queryEngine.RunTurnAsync(session, request);

        Assert.Null(result.AssistantMessage);
        Assert.Equal(QueryTerminalReason.MaxTurns, result.Terminal.Reason);
        Assert.Equal(2, result.Terminal.TurnCount);
        Assert.True(
            QueryAttachmentHelpers.TryGetMaxTurnsReachedNotification(session.Messages[^1], out var notification));
        Assert.NotNull(notification);
        Assert.Equal(1, notification!.MaxTurns);
        Assert.Equal(2, notification.TurnCount);
    }

    [Fact]
    public async Task QueryEngine_Allows_Completed_Terminal_With_Final_Tool_Result_Message()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-completed-tool-result-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var settings = new ClawSharpSettings();
        var eventSink = new InMemoryEventSink();
        var transcriptStore = new JsonlTranscriptStore();
        var queue = new InMemoryQueuedCommandQueue();
        var queryEngine = new QueryEngine(
            settings,
            eventSink,
            transcriptStore,
            new CompletedToolResultQueryTurnRunner(),
            new QueuedTaskNotificationDrainer(queue, transcriptStore));
        var session = new DefaultSessionFactory(tempDir).Create();

        var result = await queryEngine.RunTurnAsync(session, QueryTurnRequest.Create(session, "Read note.txt"));

        Assert.Null(result.AssistantMessage);
        Assert.Equal(QueryTerminalReason.Completed, result.Terminal.Reason);
        Assert.True(result.UsedTool);
        Assert.Equal(["Read"], result.ToolNames);
    }

    [Fact]
    public async Task QueryEngine_Derives_Tool_Metadata_From_Runtime_Tool_Use_Messages()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-runtime-tool-metadata-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var settings = new ClawSharpSettings();
        var eventSink = new InMemoryEventSink();
        var transcriptStore = new JsonlTranscriptStore();
        var queue = new InMemoryQueuedCommandQueue();
        var queryEngine = new QueryEngine(
            settings,
            eventSink,
            transcriptStore,
            new RuntimeToolUseQueryTurnRunner(),
            new QueuedTaskNotificationDrainer(queue, transcriptStore));
        var session = new DefaultSessionFactory(tempDir).Create();

        var result = await queryEngine.RunTurnAsync(session, QueryTurnRequest.Create(session, "hello"));

        Assert.NotNull(result.AssistantMessage);
        Assert.True(result.UsedTool);
        Assert.Equal(["Read"], result.ToolNames);
    }

    private sealed class SingleTerminalIterationRunner : IQueryIterationRunner
    {
        public async Task<QueryIterationResult> RunAsync(
            QueryTurnRequest request,
            QueryLoopState state,
            ConversationSession session,
            ClawSharpSettings settings,
            Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
            CancellationToken cancellationToken = default)
        {
            await emitEvent(
                new QueryMessageRuntimeEvent(ChatMessageFactory.CreateText(MessageRole.Assistant, "done")),
                cancellationToken);
            return new QueryTerminalIterationResult(
                new QueryLoopTerminal(QueryTerminalReason.Completed),
                state);
        }
    }

    private sealed class ContinueThenTerminalIterationRunner : IQueryIterationRunner
    {
        private int _count;

        public async Task<QueryIterationResult> RunAsync(
            QueryTurnRequest request,
            QueryLoopState state,
            ConversationSession session,
            ClawSharpSettings settings,
            Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
            CancellationToken cancellationToken = default)
        {
            _count++;
            await emitEvent(
                new QueryMessageRuntimeEvent(ChatMessageFactory.CreateText(MessageRole.Assistant, $"iteration-{_count}")),
                cancellationToken);

            if (_count == 1)
            {
                return new QueryContinueIterationResult(
                    new QueryLoopTransition(QueryContinueReason.NextTurn),
                    request with { RequestedTools = [] },
                    state with
                    {
                        TurnCount = state.TurnCount + 1,
                        Transition = new QueryLoopTransition(QueryContinueReason.NextTurn)
                    });
            }

            return new QueryTerminalIterationResult(
                new QueryLoopTerminal(QueryTerminalReason.Completed),
                state);
        }
    }

    private sealed class CompletedToolResultQueryTurnRunner : IQueryTurnRunner
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
                    [("tooluse-note", "Read", "note.txt")]));
            yield return new QueryMessageRuntimeEvent(
                ChatMessageFactory.CreateToolResult("tooluse-note", "Read", "hello"));
            yield return new QueryLoopTerminalRuntimeEvent(
                new QueryLoopTerminal(QueryTerminalReason.Completed),
                QueryLoopStateFactory.CreateInitial(session.Messages.ToArray()));
            await Task.CompletedTask;
        }
    }

    private sealed class RuntimeToolUseQueryTurnRunner : IQueryTurnRunner
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
                    [("tooluse-note", "Read", "note.txt")]));
            yield return new QueryMessageRuntimeEvent(
                ChatMessageFactory.CreateToolResult("tooluse-note", "Read", "hello"));
            yield return new QueryMessageRuntimeEvent(
                ChatMessageFactory.CreateText(MessageRole.Assistant, "Done."));
            yield return new QueryLoopTerminalRuntimeEvent(
                new QueryLoopTerminal(QueryTerminalReason.Completed),
                QueryLoopStateFactory.CreateInitial(session.Messages.ToArray()));
            await Task.CompletedTask;
        }
    }

    private sealed class MaxTurnsWithoutAssistantQueryTurnRunner : IQueryTurnRunner
    {
        public async IAsyncEnumerable<QueryRuntimeEvent> RunAsync(
            QueryTurnRequest request,
            ConversationSession session,
            ClawSharpSettings settings,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new QueryRequestStartRuntimeEvent();
            yield return new QueryMessageRuntimeEvent(
                ChatMessageFactory.CreateMaxTurnsReachedAttachmentMessage(1, 2));
            yield return new QueryLoopTerminalRuntimeEvent(
                new QueryLoopTerminal(QueryTerminalReason.MaxTurns, TurnCount: 2),
                QueryLoopStateFactory.CreateInitial(session.Messages.ToArray()));
            await Task.CompletedTask;
        }
    }

    private sealed class ContinueToModelBackedIterationRunner : IQueryIterationRunner
    {
        public async Task<QueryIterationResult> RunAsync(
            QueryTurnRequest request,
            QueryLoopState state,
            ConversationSession session,
            ClawSharpSettings settings,
            Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
            CancellationToken cancellationToken = default)
        {
            await emitEvent(
                new QueryMessageRuntimeEvent(ChatMessageFactory.CreateText(MessageRole.User, "stop-hook-blocking")),
                cancellationToken);

            return new QueryContinueIterationResult(
                new QueryLoopTransition(QueryContinueReason.StopHookBlocking),
                request with
                {
                    RequestedTools = [],
                    ExecutionMode = QueryTurnExecutionMode.ModelBacked
                },
                state with
                {
                    TurnCount = state.TurnCount + 1,
                    Transition = new QueryLoopTransition(QueryContinueReason.StopHookBlocking),
                    StopHookActive = true
                });
        }
    }

    private sealed class TerminalModelBackedIterationRunner : IQueryIterationRunner
    {
        public async Task<QueryIterationResult> RunAsync(
            QueryTurnRequest request,
            QueryLoopState state,
            ConversationSession session,
            ClawSharpSettings settings,
            Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(QueryTurnExecutionMode.ModelBacked, request.ExecutionMode);
            await emitEvent(
                new QueryMessageRuntimeEvent(ChatMessageFactory.CreateText(MessageRole.Assistant, "model-iteration")),
                cancellationToken);
            return new QueryTerminalIterationResult(
                new QueryLoopTerminal(QueryTerminalReason.Completed),
                state);
        }
    }

    private sealed class BlockingStopHookRunner : IQueryStopHookRunner
    {
        public async Task<QueryStopHookExecutionResult> RunAsync(
            QueryTurnRequest request,
            QueryLoopState state,
            ConversationSession session,
            ClawSharpSettings settings,
            Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
            CancellationToken cancellationToken = default)
        {
            var message = ChatMessageFactory.CreateUserMessage(
                "Stop hook feedback:\nblocked by hook",
                isMeta: true);
            await emitEvent(new QueryMessageRuntimeEvent(message), cancellationToken);
            return new QueryStopHookExecutionResult(
                PreventContinuation: false,
                [message]);
        }
    }

    private sealed class QueryLoopRunnerReadTool : IClawSharpTool
    {
        public QueryLoopRunnerReadTool()
        {
            Descriptor = new ToolDescriptor("LoopRead", "Tool used by query loop continuation tests.");
        }

        public ToolDescriptor Descriptor { get; }

        public bool IsEnabled() => true;

        public bool IsConcurrencySafe(string arguments) => false;

        public bool IsReadOnly(string arguments) => true;

        public string? RenderToolUseProgressMessage(IReadOnlyList<ToolProgressUpdate> progressMessages) => null;

        public string? RenderToolResultMessage(string content, System.Text.Json.Nodes.JsonNode? structuredOutput, IReadOnlyList<ToolProgressUpdate> progressMessages) => null;

        public Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ToolValidationResult.Valid());
        }

        public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ToolExecutionResult(true, "tool-output"));
        }
    }
}
