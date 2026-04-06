// TS parity status: focused C# coverage for the explicit-tool aborted_tools branch; the broader model-backed interrupt and queued-submit semantics remain intentionally unported.
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Query;
using ClawSharp.Tasks;
using ClawSharp.Tools;
using System.Text.Json.Nodes;

namespace ClawSharp.UnitTests;

public sealed class ExplicitToolIterationRunnerTests
{
    [Fact]
    public async Task QueryEngine_ExplicitToolTurn_Returns_AbortedTools_And_Interruption_Message_On_Cancelled_Tool()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-aborted-tools-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var taskRegistry = new TaskRegistry();
        var settings = new ClawSharpSettings();
        var eventSink = new InMemoryEventSink();
        var toolRegistry = new ToolRegistry(tempDir, taskRegistry);
        toolRegistry.RegisterOrReplace(new CancelledTool());
        var transcriptStore = new JsonlTranscriptStore();
        var queryTurnRunner = new ExplicitToolTurnRunner(new ToolOrchestrator(toolRegistry, eventSink));
        var queue = new InMemoryQueuedCommandQueue();
        var queryEngine = new QueryEngine(settings, eventSink, transcriptStore, queryTurnRunner, new QueuedTaskNotificationDrainer(queue, transcriptStore));
        var session = new DefaultSessionFactory(tempDir).Create();

        var request = new QueryTurnRequest(
            session.Id,
            "Run AbortTool",
            DateTimeOffset.UtcNow,
            [new ToolCallRequest("tooluse-abort", "AbortTool", "cancel")],
            MaxTurns: null,
            AbortReason: QueryAbortReason.Cancellation);

        var result = await queryEngine.RunTurnAsync(session, request);

        Assert.Null(result.AssistantMessage);
        Assert.Equal(QueryTerminalReason.AbortedTools, result.Terminal.Reason);
        Assert.Equal(3, session.Messages.Count);
        Assert.Equal(MessageRole.User, session.Messages[0].Role);
        Assert.Equal(MessageRole.Assistant, session.Messages[1].Role);
        Assert.Equal(MessageRole.User, session.Messages[2].Role);
        Assert.Equal(ChatMessageFactory.InterruptMessageForToolUse, session.Messages[2].Content);
    }

    [Fact]
    public async Task QueryEngine_ExplicitToolTurn_Skips_Interruption_Message_For_Interrupt_Abort_Reason()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-aborted-tools-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var taskRegistry = new TaskRegistry();
        var settings = new ClawSharpSettings();
        var eventSink = new InMemoryEventSink();
        var toolRegistry = new ToolRegistry(tempDir, taskRegistry);
        toolRegistry.RegisterOrReplace(new CancelledTool());
        var transcriptStore = new JsonlTranscriptStore();
        var queryTurnRunner = new ExplicitToolTurnRunner(new ToolOrchestrator(toolRegistry, eventSink));
        var queue = new InMemoryQueuedCommandQueue();
        var queryEngine = new QueryEngine(settings, eventSink, transcriptStore, queryTurnRunner, new QueuedTaskNotificationDrainer(queue, transcriptStore));
        var session = new DefaultSessionFactory(tempDir).Create();

        var request = new QueryTurnRequest(
            session.Id,
            "Run AbortTool",
            DateTimeOffset.UtcNow,
            [new ToolCallRequest("tooluse-abort", "AbortTool", "interrupt")],
            MaxTurns: null,
            AbortReason: QueryAbortReason.Interrupt);

        var result = await queryEngine.RunTurnAsync(session, request);

        Assert.Null(result.AssistantMessage);
        Assert.Equal(QueryTerminalReason.AbortedTools, result.Terminal.Reason);
        Assert.Equal(2, session.Messages.Count);
        Assert.Equal(MessageRole.User, session.Messages[0].Role);
        Assert.Equal(MessageRole.Assistant, session.Messages[1].Role);
    }

    [Fact]
    public async Task QueryEngine_ExplicitToolTurn_Returns_HookStopped_When_Tool_Emits_HookStoppedContinuation_Attachment()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-hook-stopped-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var taskRegistry = new TaskRegistry();
        var settings = new ClawSharpSettings();
        var eventSink = new InMemoryEventSink();
        var toolRegistry = new ToolRegistry(tempDir, taskRegistry);
        toolRegistry.RegisterOrReplace(new HookStoppedTool());
        var transcriptStore = new JsonlTranscriptStore();
        var queryTurnRunner = new ExplicitToolTurnRunner(new ToolOrchestrator(toolRegistry, eventSink));
        var queue = new InMemoryQueuedCommandQueue();
        var queryEngine = new QueryEngine(settings, eventSink, transcriptStore, queryTurnRunner, new QueuedTaskNotificationDrainer(queue, transcriptStore));
        var session = new DefaultSessionFactory(tempDir).Create();

        var request = new QueryTurnRequest(
            session.Id,
            "Run HookStopTool",
            DateTimeOffset.UtcNow,
            [new ToolCallRequest("tooluse-hook-stop", "HookStopTool", "hook stop")]);

        var result = await queryEngine.RunTurnAsync(session, request);

        Assert.Null(result.AssistantMessage);
        Assert.Equal(QueryTerminalReason.HookStopped, result.Terminal.Reason);
        Assert.Equal(4, session.Messages.Count);
        Assert.Equal(MessageRole.User, session.Messages[0].Role);
        Assert.Equal(MessageRole.Assistant, session.Messages[1].Role);
        Assert.Equal(MessageRole.System, session.Messages[2].Role);
        Assert.Equal(MessageRole.User, session.Messages[3].Role);
        Assert.True(QueryAttachmentHelpers.TryGetHookStoppedContinuationAttachment(session.Messages[2], out var attachment));
        Assert.NotNull(attachment);
        Assert.Equal("Stop after hook.", attachment!.Message);
        Assert.Equal("HookStopTool", session.Messages[3].ContentBlocks[0].Name);
    }

    [Fact]
    public async Task QueryEngine_ExplicitToolTurn_Carries_Updated_Tool_Use_Context_In_Result_State()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-tool-use-context-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "note.txt");
        await File.WriteAllTextAsync(filePath, "hello");

        var taskRegistry = new TaskRegistry();
        var settings = new ClawSharpSettings();
        var eventSink = new InMemoryEventSink();
        var toolRegistry = new ToolRegistry(tempDir, taskRegistry);
        var transcriptStore = new JsonlTranscriptStore();
        var queryTurnRunner = new ExplicitToolTurnRunner(new ToolOrchestrator(toolRegistry, eventSink));
        var queue = new InMemoryQueuedCommandQueue();
        var queryEngine = new QueryEngine(settings, eventSink, transcriptStore, queryTurnRunner, new QueuedTaskNotificationDrainer(queue, transcriptStore), toolRegistry: toolRegistry);
        var session = new DefaultSessionFactory(tempDir).Create();

        var request = QueryTurnRequest.Create(
            session,
            "Read note.txt",
            [new ToolCallRequest("tooluse-note", "Read", "note.txt")]);

        var result = await queryEngine.RunTurnAsync(session, request);

        Assert.NotNull(result.AssistantMessage);
        Assert.True(result.State.ToolUseContext.ReadFileState.Has(filePath));
        Assert.Equal("hello", result.State.ToolUseContext.ReadFileState.Get(filePath)?.Content);
        Assert.Equal(toolRegistry.AppStateStore.GetState().ToolPermissionContext, result.State.ToolUseContext.ToolPermissionContext);
        Assert.Equal(toolRegistry.AppStateStore.GetState().MainLoopModel, result.State.ToolUseContext.MainLoopModel);
    }

    [Fact]
    public async Task QueryEngine_ExplicitToolTurn_Runs_Stop_Hooks_And_Returns_StopHookPrevented()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-stop-hooks-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var taskRegistry = new TaskRegistry();
        var settings = new ClawSharpSettings();
        var eventSink = new InMemoryEventSink();
        var appStateStore = new ClawSharpAppStateStore(
            ClawSharpAppState.CreateDefault(
                tempDir,
                new StartupEnvironment(tempDir, BareMode: false, DisablePolicySkills: false),
                settings,
                [],
                [],
                [],
                [],
                [],
                [
                    new HookDefinition(
                        HookEvent.Stop,
                        "settings",
                        null,
                        new HookCommandDefinition(
                            HookKind.Command,
                            Command: "echo '{\"continue\":false,\"stopReason\":\"Stopped by stop hook\",\"systemMessage\":\"Stop warning\"}'",
                            Shell: HookShell.Bash))
                ]));
        var toolRegistry = new ToolRegistry(tempDir, taskRegistry, appStateStore: appStateStore);
        var transcriptStore = new JsonlTranscriptStore();
        var queryTurnRunner = new ExplicitToolTurnRunner(
            new ToolOrchestrator(toolRegistry, eventSink),
            new QueryStopHookRunner(toolRegistry));
        var queue = new InMemoryQueuedCommandQueue();
        var queryEngine = new QueryEngine(settings, eventSink, transcriptStore, queryTurnRunner, new QueuedTaskNotificationDrainer(queue, transcriptStore));
        var session = new DefaultSessionFactory(tempDir).Create();

        File.WriteAllText(Path.Combine(tempDir, "missing.txt"), "hello");

        var request = QueryTurnRequest.Create(
            session,
            "Read note.txt",
            [new ToolCallRequest("tooluse-read", "Read", "missing.txt")]);

        var result = await queryEngine.RunTurnAsync(session, request);

        Assert.Null(result.AssistantMessage);
        Assert.Equal(QueryTerminalReason.StopHookPrevented, result.Terminal.Reason);
        Assert.Contains(
            session.Messages,
            message => QueryAttachmentHelpers.TryGetHookSystemMessageAttachment(message, out var attachment) &&
                       attachment is not null &&
                       attachment.HookEvent == HookEvent.Stop &&
                       attachment.Content == "Stop warning");
        Assert.Contains(
            session.Messages,
            message => QueryAttachmentHelpers.TryGetHookStoppedContinuationAttachment(message, out var attachment) &&
                       attachment is not null &&
                       attachment.HookEvent == HookEvent.Stop &&
                       attachment.Message == "Stopped by stop hook");
        Assert.Contains(
            session.Messages,
            message => message.Role == MessageRole.System &&
                       message.ContentBlocks.Count == 1 &&
                       message.ContentBlocks[0].Kind == MessageContentKind.Text &&
                       message.ContentBlocks[0].Metadata?.TryGetValue("subtype", out var subtype) == true &&
                       subtype == "stop_hook_summary");
    }

    [Fact]
    public async Task ExplicitToolIterationRunner_StopHookBlocking_Continuation_Preserves_Model_Turn_Context()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-stop-hook-blocking-context-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var taskRegistry = new TaskRegistry();
        var settings = new ClawSharpSettings();
        var eventSink = new InMemoryEventSink();
        var toolRegistry = new ToolRegistry(tempDir, taskRegistry);
        var orchestrator = new ToolOrchestrator(toolRegistry, eventSink);
        var runner = new ExplicitToolIterationRunner(orchestrator, new BlockingStopHookRunner());
        var session = new DefaultSessionFactory(tempDir).Create();
        var request = QueryTurnRequest.Create(
            session,
            "Read note.txt",
            [new ToolCallRequest("tooluse-read", "Read", "missing.txt")]) with
        {
            ModelTurnContext = new QueryModelTurnContext(
                ["system prompt"],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["cwd"] = tempDir
                },
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["platform"] = "windows"
                },
                "repl_main_thread:outputStyle:custom")
        };

        var result = await runner.RunAsync(
            request,
            QueryLoopStateFactory.CreateInitial([], QueryToolUseContextState.Empty),
            session,
            settings,
            static (_, _) => Task.CompletedTask);

        var continuation = Assert.IsType<QueryContinueIterationResult>(result);
        Assert.Equal(QueryTurnExecutionMode.ModelBacked, continuation.NextRequest.ExecutionMode);
        Assert.Same(request.ModelTurnContext, continuation.NextRequest.ModelTurnContext);
        Assert.Equal("repl_main_thread:outputStyle:custom", continuation.NextRequest.ModelTurnContext!.QuerySource);
    }

    private sealed class CancelledTool : IClawSharpTool
    {
        public CancelledTool()
        {
            Descriptor = new ToolDescriptor("AbortTool", "Tool that simulates query-loop cancellation.");
        }

        public ToolDescriptor Descriptor { get; }

        public bool IsEnabled() => true;

        public bool IsConcurrencySafe(string arguments) => false;

        public bool IsReadOnly(string arguments) => false;

        public string? RenderToolUseProgressMessage(IReadOnlyList<ToolProgressUpdate> progressMessages) => null;

        public string? RenderToolResultMessage(string content, JsonNode? structuredOutput, IReadOnlyList<ToolProgressUpdate> progressMessages) => null;

        public Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ToolValidationResult.Valid());
        }

        public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
        {
            throw new OperationCanceledException("Simulated tool cancellation.");
        }
    }

    private sealed class HookStoppedTool : IClawSharpTool
    {
        public HookStoppedTool()
        {
            Descriptor = new ToolDescriptor("HookStopTool", "Tool that emits a hook-stopped continuation attachment.");
        }

        public ToolDescriptor Descriptor { get; }

        public bool IsEnabled() => true;

        public bool IsConcurrencySafe(string arguments) => false;

        public bool IsReadOnly(string arguments) => false;

        public string? RenderToolUseProgressMessage(IReadOnlyList<ToolProgressUpdate> progressMessages) => null;

        public string? RenderToolResultMessage(string content, JsonNode? structuredOutput, IReadOnlyList<ToolProgressUpdate> progressMessages) => null;

        public Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ToolValidationResult.Valid());
        }

        public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
        {
            context.ReportMessage(
                ChatMessageFactory.CreateHookStoppedContinuationAttachmentMessage(
                    "Stop after hook.",
                    "test-hook",
                    "tooluse-hook-stop",
                    HookEvent.PostToolUse));
            return Task.FromResult(new ToolExecutionResult(true, "hook stopped tool completed"));
        }
    }

    private sealed class BlockingStopHookRunner : IQueryStopHookRunner
    {
        public Task<QueryStopHookExecutionResult> RunAsync(
            QueryTurnRequest request,
            QueryLoopState state,
            ConversationSession session,
            ClawSharpSettings settings,
            Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new QueryStopHookExecutionResult(
                    PreventContinuation: false,
                    [
                        ChatMessageFactory.CreateUserMessage(
                            "Stop hook feedback:\nblocked by hook",
                            isMeta: true)
                    ]));
        }
    }
}
