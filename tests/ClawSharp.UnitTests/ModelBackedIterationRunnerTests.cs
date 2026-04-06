// TS origin: ./query.ts, ./query/deps.ts, ./services/api/claude.ts
// TS parity status: focused coverage for the streamed model-call executor boundary under the C# model-backed iteration runner; live API transport, fallback retry execution, and real assistant sampling remain intentionally unported.
using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Query;
using ClawSharp.Tasks;
using ClawSharp.Tools;

namespace ClawSharp.UnitTests;

public sealed class ModelBackedIterationRunnerTests
{
    [Fact]
    public async Task RunAsync_Emits_Streamed_Model_Updates_And_Returns_Completed_Attempt_Result()
    {
        var postSamplingRegistry = new RecordingPostSamplingHookRegistry();
        var runner = new ModelBackedIterationRunner(
            postSamplingRegistry,
            new FakeIterationRequestBuilder(),
            new FakeCompletedModelCallExecutor());
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-backed-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-model-1", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var emittedEvents = new List<QueryRuntimeEvent>();

        var result = await runner.RunAsync(
            QueryTurnRequest.Create(session, "hello"),
            QueryLoopStateFactory.CreateInitial([]),
            session,
            new ClawSharpSettings(),
            (runtimeEvent, _) =>
            {
                emittedEvents.Add(runtimeEvent);
                return Task.CompletedTask;
            });

        Assert.Equal(3, emittedEvents.Count);
        Assert.IsType<QueryStreamEventRuntimeEvent>(emittedEvents[0]);
        Assert.IsType<QueryStreamDeltaRuntimeEvent>(emittedEvents[1]);
        var messageEvent = Assert.IsType<QueryMessageRuntimeEvent>(emittedEvents[2]);
        Assert.Equal("model-complete", messageEvent.Message.Content);

        var terminal = Assert.IsType<QueryTerminalIterationResult>(result);
        Assert.Equal(QueryTerminalReason.Completed, terminal.Terminal.Reason);
        var hookContext = Assert.Single(postSamplingRegistry.Contexts);
        Assert.Equal("repl_main_thread", hookContext.QuerySource);
        var hookMessage = Assert.Single(hookContext.Messages);
        Assert.Equal("model-complete", hookMessage.Content);
    }

    [Fact]
    public async Task RunAsync_Preserves_NotImplemented_Boundary_For_Default_Model_Call_Executor()
    {
        var runner = new ModelBackedIterationRunner(
            new PostSamplingHookRegistry(),
            new FakeIterationRequestBuilder());
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-backed-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-model-2", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));

        await Assert.ThrowsAsync<QueryExecutionNotImplementedException>(() =>
            runner.RunAsync(
                QueryTurnRequest.Create(session, "hello"),
                QueryLoopStateFactory.CreateInitial([]),
                session,
                new ClawSharpSettings(),
                static (_, _) => Task.CompletedTask));
    }

    [Fact]
    public async Task RunAsync_Builds_Per_Iteration_Streaming_Request_From_Current_State()
    {
        var requestBuilder = new FakeIterationRequestBuilder();
        var executor = new CapturingModelCallExecutor();
        var runner = new ModelBackedIterationRunner(
            new PostSamplingHookRegistry(),
            requestBuilder,
            executor);
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-backed-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-model-3", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var request = QueryTurnRequest.Create(session, "hello") with
        {
            ModelTurnContext = new QueryModelTurnContext(
                ["system prompt"],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["cwd"] = sessionRoot
                },
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["platform"] = "windows"
                },
                "repl_main_thread:outputStyle:custom")
        };
        var state = QueryLoopStateFactory.CreateInitial(
            [ChatMessageFactory.CreateText(MessageRole.User, "hello")]);

        await runner.RunAsync(
            request,
            state,
            session,
            new ClawSharpSettings(),
            static (_, _) => Task.CompletedTask);

        Assert.NotNull(executor.CapturedRequest);
        Assert.Equal("repl_main_thread:outputStyle:custom", executor.CapturedRequest!.QuerySource);
        Assert.Equal("built-from-state", executor.CapturedRequest.Request.Model);
        Assert.Equal("hello", executor.CapturedRequest.Request.Messages[0].Content[0].Text);
        Assert.Equal("system prompt", executor.CapturedRequest.Request.System[0].Text);
    }

    [Fact]
    public async Task RunAsync_Retries_In_Same_Iteration_When_Model_Fallback_Is_Requested()
    {
        var executor = new FallbackThenCompleteModelCallExecutor();
        var runner = new ModelBackedIterationRunner(
            new PostSamplingHookRegistry(),
            new QueryModelIterationRequestBuilder(),
            executor);
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-backed-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-model-4", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var request = QueryTurnRequest.Create(session, "hello");
        var state = QueryLoopStateFactory.CreateInitial(
            [ChatMessageFactory.CreateText(MessageRole.User, "hello")],
            QueryToolUseContextState.Empty with { MainLoopModel = "primary-model" });
        var settings = new ClawSharpSettings
        {
            Runtime = new RuntimeSettings
            {
                Model = "settings-model"
            }
        };
        var emittedEvents = new List<QueryRuntimeEvent>();

        var result = await runner.RunAsync(
            request,
            state,
            session,
            settings,
            (runtimeEvent, _) =>
            {
                emittedEvents.Add(runtimeEvent);
                return Task.CompletedTask;
            });

        Assert.Equal(2, executor.CapturedRequests.Count);
        Assert.Equal("primary-model", executor.CapturedRequests[0].Request.Model);
        Assert.Equal("fallback-model", executor.CapturedRequests[1].Request.Model);
        var warningEvent = Assert.IsType<QueryMessageRuntimeEvent>(emittedEvents[0]);
        Assert.Equal(MessageRole.System, warningEvent.Message.Role);
        Assert.Contains("Switched to fallback-model due to high demand for primary-model", warningEvent.Message.Content, StringComparison.Ordinal);
        var terminal = Assert.IsType<QueryTerminalIterationResult>(result);
        Assert.Equal(QueryTerminalReason.Completed, terminal.Terminal.Reason);
    }

    [Fact]
    public async Task RunAsync_Continues_With_NextTurn_When_Model_Attempt_Emits_ToolUse()
    {
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-backed-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var taskRegistry = new TaskRegistry();
        var eventSink = new InMemoryEventSink();
        var toolRegistry = new ToolRegistry(sessionRoot, taskRegistry);
        toolRegistry.RegisterOrReplace(new ModelBackedToolUseTestTool());
        var summaryGenerator = new RecordingToolUseSummaryGenerator();
        var runner = new ModelBackedIterationRunner(
            new PostSamplingHookRegistry(),
            new QueryModelIterationRequestBuilder(),
            new CompletedToolUseModelCallExecutor(),
            toolOrchestrator: new ToolOrchestrator(toolRegistry, eventSink),
            toolUseSummaryGenerator: summaryGenerator);
        var session = new ConversationSession("session-model-tooluse", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var emittedEvents = new List<QueryRuntimeEvent>();
        var state = QueryLoopStateFactory.CreateInitial(
            [ChatMessageFactory.CreateText(MessageRole.User, "hello")],
            QueryToolUseContextStateFactory.CreateFromToolRegistry(toolRegistry));

        var result = await runner.RunAsync(
            QueryTurnRequest.Create(session, "hello"),
            state,
            session,
            new ClawSharpSettings(),
            (runtimeEvent, _) =>
            {
                emittedEvents.Add(runtimeEvent);
                return Task.CompletedTask;
            });

        var continuation = Assert.IsType<QueryContinueIterationResult>(result);
        Assert.Equal(QueryContinueReason.NextTurn, continuation.Transition.Reason);
        Assert.Equal(QueryTurnExecutionMode.ModelBacked, continuation.NextRequest.ExecutionMode);
        Assert.Empty(continuation.NextRequest.RequestedTools);
        Assert.Equal(2, continuation.State.TurnCount);
        Assert.NotNull(continuation.State.PendingToolUseSummary);

        var messageEvents = emittedEvents.OfType<QueryMessageRuntimeEvent>().Select(queryEvent => queryEvent.Message).ToArray();
        Assert.Single(
            messageEvents,
            message => message.Role == MessageRole.Assistant &&
                       message.ContentBlocks.Any(block => block.Kind == MessageContentKind.ToolUse));
        var toolResultMessage = Assert.Single(
            messageEvents,
                message => message.Role == MessageRole.User &&
                           message.ContentBlocks.Any(block => block.Kind == MessageContentKind.ToolResult));
        Assert.Equal("tooluse-model-1", toolResultMessage.ContentBlocks[0].Metadata?["toolUseId"]);
        Assert.Equal("model tool output", toolResultMessage.Content);

        var summary = await continuation.State.PendingToolUseSummary!;
        Assert.NotNull(summary);
        Assert.Equal("summary:model tool output", summary!.Summary);
        Assert.Equal(["tooluse-model-1"], summary.PrecedingToolUseIds);
        Assert.NotNull(summaryGenerator.LastRequest);
        Assert.Equal("tooluse-model-1", Assert.Single(summaryGenerator.LastRequest!.Tools).ToolUseId);
        Assert.Equal("ModelTool", summaryGenerator.LastRequest.Tools[0].ToolName);
        Assert.Equal("{\"path\":\"note.txt\"}", summaryGenerator.LastRequest.Tools[0].Input);
        Assert.Equal("model tool output", summaryGenerator.LastRequest.Tools[0].Output);
        Assert.Null(summaryGenerator.LastRequest.LastAssistantText);
    }

    [Fact]
    public async Task RunAsync_Does_Not_Run_Stop_Hooks_Before_PostTool_FollowUp_Turn()
    {
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-backed-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var taskRegistry = new TaskRegistry();
        var eventSink = new InMemoryEventSink();
        var toolRegistry = new ToolRegistry(sessionRoot, taskRegistry);
        toolRegistry.RegisterOrReplace(new ModelBackedToolUseTestTool());
        var stopHookRunner = new RecordingStopHookRunner(
            new QueryStopHookExecutionResult(
                PreventContinuation: false,
                [
                    ChatMessageFactory.CreateUserMessage(
                        "Stop hook feedback:\nblocked by hook",
                        isMeta: true)
                ]));
        var runner = new ModelBackedIterationRunner(
            new PostSamplingHookRegistry(),
            new QueryModelIterationRequestBuilder(),
            new CompletedToolUseModelCallExecutor(),
            toolOrchestrator: new ToolOrchestrator(toolRegistry, eventSink),
            stopHookRunner: stopHookRunner);
        var session = new ConversationSession("session-model-tooluse-stop-hook", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));

        var result = await runner.RunAsync(
            QueryTurnRequest.Create(session, "hello"),
            QueryLoopStateFactory.CreateInitial(
                [ChatMessageFactory.CreateText(MessageRole.User, "hello")],
                QueryToolUseContextStateFactory.CreateFromToolRegistry(toolRegistry)),
            session,
            new ClawSharpSettings(),
            static (_, _) => Task.CompletedTask);

        var continuation = Assert.IsType<QueryContinueIterationResult>(result);
        Assert.Equal(QueryContinueReason.NextTurn, continuation.Transition.Reason);
        Assert.Equal(0, stopHookRunner.CallCount);
        Assert.DoesNotContain(
            continuation.State.Messages,
            message => string.Equals(message.Content, "Stop hook feedback:\nblocked by hook", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_Emits_Pending_Tool_Use_Summary_From_Previous_Turn_Before_Terminal_Result()
    {
        var runner = new ModelBackedIterationRunner(
            new PostSamplingHookRegistry(),
            new QueryModelIterationRequestBuilder(),
            new CompletedAssistantMessageModelCallExecutor());
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-backed-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-model-summary", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var pendingSummary = Task.FromResult<QueryToolUseSummaryMessage?>(
            QueryToolUseSummaryMessageFactory.Create(
                "Read note.txt",
                ["tooluse-note"]));
        var emittedEvents = new List<QueryRuntimeEvent>();

        var result = await runner.RunAsync(
            QueryTurnRequest.Create(session, "hello"),
            QueryLoopStateFactory.CreateInitial(
                [ChatMessageFactory.CreateText(MessageRole.User, "hello")],
                pendingToolUseSummary: pendingSummary),
            session,
            new ClawSharpSettings(),
            (runtimeEvent, _) =>
            {
                emittedEvents.Add(runtimeEvent);
                return Task.CompletedTask;
            });

        var terminal = Assert.IsType<QueryTerminalIterationResult>(result);
        Assert.Equal(QueryTerminalReason.Completed, terminal.Terminal.Reason);
        var summaryEvent = Assert.IsType<QueryToolUseSummaryRuntimeEvent>(
            emittedEvents.Single(queryEvent => queryEvent is QueryToolUseSummaryRuntimeEvent));
        Assert.Equal("Read note.txt", summaryEvent.Message.Summary);
        Assert.Equal(["tooluse-note"], summaryEvent.Message.PrecedingToolUseIds);
        Assert.Null(terminal.State.PendingToolUseSummary);
    }

    [Fact]
    public async Task RunAsync_Continues_With_MaxOutputTokens_Escalation_When_Withheld_Error_Is_Eligible()
    {
        var executor = new WithheldMaxOutputTokensModelCallExecutor();
        var runner = new ModelBackedIterationRunner(
            new PostSamplingHookRegistry(),
            new QueryModelIterationRequestBuilder(),
            executor);
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-backed-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-model-5", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var request = QueryTurnRequest.Create(session, "hello");
        var state = QueryLoopStateFactory.CreateInitial(
            [ChatMessageFactory.CreateText(MessageRole.User, "hello")]);

        var result = await runner.RunAsync(
            request,
            state,
            session,
            new ClawSharpSettings(),
            static (_, _) => Task.CompletedTask);

        var continueResult = Assert.IsType<QueryContinueIterationResult>(result);
        Assert.Equal(QueryContinueReason.MaxOutputTokensEscalate, continueResult.Transition.Reason);
        Assert.Equal(QueryMaxOutputTokensRecoveryPolicy.EscalatedMaxTokens, continueResult.State.MaxOutputTokensOverride);
        Assert.Equal(0, continueResult.State.MaxOutputTokensRecoveryCount);
        Assert.Single(continueResult.State.Messages);
    }

    [Fact]
    public async Task RunAsync_Continues_With_MaxOutputTokens_Recovery_Message_After_Escalation()
    {
        var executor = new WithheldMaxOutputTokensModelCallExecutor();
        var runner = new ModelBackedIterationRunner(
            new PostSamplingHookRegistry(),
            new QueryModelIterationRequestBuilder(),
            executor);
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-backed-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-model-6", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var request = QueryTurnRequest.Create(session, "hello");
        var state = QueryLoopStateFactory.CreateInitial(
            [ChatMessageFactory.CreateText(MessageRole.User, "hello")],
            maxOutputTokensOverride: QueryMaxOutputTokensRecoveryPolicy.EscalatedMaxTokens) with
        {
            MaxOutputTokensRecoveryCount = 1
        };

        var result = await runner.RunAsync(
            request,
            state,
            session,
            new ClawSharpSettings(),
            static (_, _) => Task.CompletedTask);

        var continueResult = Assert.IsType<QueryContinueIterationResult>(result);
        Assert.Equal(QueryContinueReason.MaxOutputTokensRecovery, continueResult.Transition.Reason);
        Assert.Equal(2, continueResult.Transition.Attempt);
        Assert.Null(continueResult.State.MaxOutputTokensOverride);
        Assert.Equal(2, continueResult.State.MaxOutputTokensRecoveryCount);
        Assert.Equal(QueryMaxOutputTokensRecoveryPolicy.RecoveryMessageContent, continueResult.State.Messages[^1].Content);
    }

    [Fact]
    public async Task RunAsync_Continues_With_Token_Budget_Nudge_And_Output_Usage_Attachment()
    {
        var runner = new ModelBackedIterationRunner(
            new PostSamplingHookRegistry(),
            new QueryModelIterationRequestBuilder(),
            new CompletedTurnOutputModelCallExecutor());
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-backed-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-model-7", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var request = QueryTurnRequest.Create(session, "hello") with
        {
            TaskBudget = new QueryTaskBudget(1_000)
        };
        var state = QueryLoopStateFactory.CreateInitial(
            [ChatMessageFactory.CreateText(MessageRole.User, "hello")]);
        var emittedEvents = new List<QueryRuntimeEvent>();

        var result = await runner.RunAsync(
            request,
            state,
            session,
            new ClawSharpSettings(),
            (runtimeEvent, _) =>
            {
                emittedEvents.Add(runtimeEvent);
                return Task.CompletedTask;
            });

        var continueResult = Assert.IsType<QueryContinueIterationResult>(result);
        Assert.Equal(QueryContinueReason.TokenBudgetContinuation, continueResult.Transition.Reason);
        Assert.NotNull(continueResult.State.TokenBudgetTracker);
        Assert.Equal(1, continueResult.State.TokenBudgetTracker!.ContinuationCount);
        Assert.Equal(
            "Stopped at 40% of token target (400 / 1,000). Keep working — do not summarize.",
            continueResult.State.Messages[^1].Content);
        Assert.True(
            QueryAttachmentHelpers.TryGetOutputTokenUsageAttachment(
                continueResult.State.Messages[^2],
                out var outputTokenUsage));
        Assert.NotNull(outputTokenUsage);
        Assert.Equal(400, outputTokenUsage!.Turn);
        Assert.Equal(400, outputTokenUsage.Session);
        Assert.Equal(1_000, outputTokenUsage.Budget);
        Assert.NotNull(continueResult.NextRequest.TaskBudget);
        Assert.Equal(1_000, continueResult.NextRequest.TaskBudget!.Total);
        Assert.Equal(600, continueResult.NextRequest.TaskBudget.Remaining);
        Assert.Contains(
            emittedEvents,
            runtimeEvent =>
                runtimeEvent is QueryMessageRuntimeEvent messageEvent &&
                QueryAttachmentHelpers.TryGetOutputTokenUsageAttachment(messageEvent.Message, out _));
    }

    [Fact]
    public async Task RunAsync_Runs_Stop_Hooks_After_Assistant_Terminal_When_No_Tool_FollowUp_Is_Pending()
    {
        var stopHookRunner = new RecordingStopHookRunner(
            new QueryStopHookExecutionResult(
                PreventContinuation: false,
                [
                    ChatMessageFactory.CreateUserMessage(
                        "Stop hook feedback:\nblocked by hook",
                        isMeta: true)
                ]));
        var runner = new ModelBackedIterationRunner(
            new PostSamplingHookRegistry(),
            new QueryModelIterationRequestBuilder(),
            new CompletedAssistantMessageModelCallExecutor(),
            stopHookRunner: stopHookRunner);
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-backed-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-model-stop-hook-terminal", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var emittedEvents = new List<QueryRuntimeEvent>();

        var result = await runner.RunAsync(
            QueryTurnRequest.Create(session, "hello"),
            QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")]),
            session,
            new ClawSharpSettings(),
            (runtimeEvent, _) =>
            {
                emittedEvents.Add(runtimeEvent);
                return Task.CompletedTask;
            });

        var continuation = Assert.IsType<QueryContinueIterationResult>(result);
        Assert.Equal(QueryContinueReason.StopHookBlocking, continuation.Transition.Reason);
        Assert.Equal(1, stopHookRunner.CallCount);
        Assert.Contains(
            emittedEvents,
            runtimeEvent =>
                runtimeEvent is QueryMessageRuntimeEvent messageEvent &&
                string.Equals(messageEvent.Message.Content, "Stop hook feedback:\nblocked by hook", StringComparison.Ordinal));
        Assert.Equal("Stop hook feedback:\nblocked by hook", continuation.State.Messages[^1].Content);
    }

    [Fact]
    public async Task RunAsync_Maps_PromptTooLong_Api_Error_To_PromptTooLong_Terminal()
    {
        var runner = new ModelBackedIterationRunner(
            new PostSamplingHookRegistry(),
            new QueryModelIterationRequestBuilder(),
            new PromptTooLongModelCallExecutor());
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-backed-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-model-8", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));

        var result = await runner.RunAsync(
            QueryTurnRequest.Create(session, "hello"),
            QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")]),
            session,
            new ClawSharpSettings(),
            static (_, _) => Task.CompletedTask);

        var terminal = Assert.IsType<QueryTerminalIterationResult>(result);
        Assert.Equal(QueryTerminalReason.PromptTooLong, terminal.Terminal.Reason);
        Assert.StartsWith("Prompt is too long", terminal.Terminal.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Maps_Media_Api_Error_To_ImageError_Terminal()
    {
        var runner = new ModelBackedIterationRunner(
            new PostSamplingHookRegistry(),
            new QueryModelIterationRequestBuilder(),
            new MediaErrorModelCallExecutor());
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-backed-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-model-9", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));

        var result = await runner.RunAsync(
            QueryTurnRequest.Create(session, "hello"),
            QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")]),
            session,
            new ClawSharpSettings(),
            static (_, _) => Task.CompletedTask);

        var terminal = Assert.IsType<QueryTerminalIterationResult>(result);
        Assert.Equal(QueryTerminalReason.ImageError, terminal.Terminal.Reason);
        Assert.Contains("Image was too large", terminal.Terminal.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Continues_With_CollapseDrainRetry_When_PromptOverflow_Recovery_Runner_Recovers()
    {
        var runner = new ModelBackedIterationRunner(
            new PostSamplingHookRegistry(),
            new QueryModelIterationRequestBuilder(),
            new PromptTooLongModelCallExecutor(),
            new CollapseDrainPromptOverflowRecoveryRunner());
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-backed-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-model-10", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var request = QueryTurnRequest.Create(session, "hello");
        var state = QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")]);

        var result = await runner.RunAsync(
            request,
            state,
            session,
            new ClawSharpSettings(),
            static (_, _) => Task.CompletedTask);

        var continueResult = Assert.IsType<QueryContinueIterationResult>(result);
        Assert.Equal(QueryContinueReason.CollapseDrainRetry, continueResult.Transition.Reason);
        Assert.Equal(QueryContinueReason.CollapseDrainRetry, continueResult.State.Transition!.Reason);
    }

    [Fact]
    public async Task RunAsync_Continues_With_ReactiveCompactRetry_When_Media_Recovery_Runner_Recovers()
    {
        var runner = new ModelBackedIterationRunner(
            new PostSamplingHookRegistry(),
            new QueryModelIterationRequestBuilder(),
            new MediaErrorModelCallExecutor(),
            new StaticReactiveCompactPromptOverflowRecoveryRunner());
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-backed-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-model-11", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var request = QueryTurnRequest.Create(session, "hello");
        var state = QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")]);

        var result = await runner.RunAsync(
            request,
            state,
            session,
            new ClawSharpSettings(),
            static (_, _) => Task.CompletedTask);

        var continueResult = Assert.IsType<QueryContinueIterationResult>(result);
        Assert.Equal(QueryContinueReason.ReactiveCompactRetry, continueResult.Transition.Reason);
        Assert.True(continueResult.State.HasAttemptedReactiveCompact);
        Assert.Equal(QueryContinueReason.ReactiveCompactRetry, continueResult.State.Transition!.Reason);
    }

    [Fact]
    public async Task RunAsync_Uses_Default_CompactBoundary_Recovery_For_PromptTooLong_When_Boundary_Exists()
    {
        var runner = new ModelBackedIterationRunner(
            new PostSamplingHookRegistry(),
            new QueryModelIterationRequestBuilder(),
            new PromptTooLongModelCallExecutor(),
            new CompactBoundaryPromptOverflowRecoveryRunner());
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-backed-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-model-12", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var boundary = ChatMessageFactory.CreateCompactBoundaryMessage("auto", 100);
        var state = QueryLoopStateFactory.CreateInitial(
            [
                ChatMessageFactory.CreateText(MessageRole.User, "old"),
                boundary,
                ChatMessageFactory.CreateText(MessageRole.User, "newer")
            ]);

        var result = await runner.RunAsync(
            QueryTurnRequest.Create(session, "hello"),
            state,
            session,
            new ClawSharpSettings(),
            static (_, _) => Task.CompletedTask);

        var continueResult = Assert.IsType<QueryContinueIterationResult>(result);
        Assert.Equal(QueryContinueReason.CollapseDrainRetry, continueResult.Transition.Reason);
        Assert.Equal(1, continueResult.Transition.Committed);
        Assert.Equal(boundary.Id, continueResult.State.Messages[0].Id);
        Assert.Equal("newer", continueResult.State.Messages[1].Content);
    }

    [Fact]
    public async Task RunAsync_Maps_Repeated_PromptOverflow_After_CollapseRetry_To_BlockingLimit()
    {
        var runner = new ModelBackedIterationRunner(
            new PostSamplingHookRegistry(),
            new QueryModelIterationRequestBuilder(),
            new PromptTooLongModelCallExecutor(),
            new CompactBoundaryPromptOverflowRecoveryRunner());
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-backed-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-model-13", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var boundary = ChatMessageFactory.CreateCompactBoundaryMessage("auto", 100);
        var state = QueryLoopStateFactory.CreateInitial(
            [
                ChatMessageFactory.CreateText(MessageRole.User, "old"),
                boundary,
                ChatMessageFactory.CreateText(MessageRole.User, "newer")
            ]) with
        {
            Transition = new QueryLoopTransition(QueryContinueReason.CollapseDrainRetry, Committed: 1)
        };

        var result = await runner.RunAsync(
            QueryTurnRequest.Create(session, "hello"),
            state,
            session,
            new ClawSharpSettings(),
            static (_, _) => Task.CompletedTask);

        var terminal = Assert.IsType<QueryTerminalIterationResult>(result);
        Assert.Equal(QueryTerminalReason.BlockingLimit, terminal.Terminal.Reason);
        Assert.StartsWith("Prompt is too long", terminal.Terminal.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Uses_ReactiveCompact_PostCompactMessages_When_Executor_Returns_Result()
    {
        var runner = new ModelBackedIterationRunner(
            new PostSamplingHookRegistry(),
            new QueryModelIterationRequestBuilder(),
            new MediaErrorModelCallExecutor(),
            new ReactiveCompactPromptOverflowRecoveryRunner(new FakeReactiveCompactExecutor()));
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-backed-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-model-14", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var state = QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")]);
        var emittedEvents = new List<QueryRuntimeEvent>();

        var result = await runner.RunAsync(
            QueryTurnRequest.Create(session, "hello"),
            state,
            session,
            new ClawSharpSettings(),
            (runtimeEvent, _) =>
            {
                emittedEvents.Add(runtimeEvent);
                return Task.CompletedTask;
            });

        var continueResult = Assert.IsType<QueryContinueIterationResult>(result);
        Assert.Equal(QueryContinueReason.ReactiveCompactRetry, continueResult.Transition.Reason);
        Assert.True(continueResult.State.HasAttemptedReactiveCompact);
        Assert.Equal(5, continueResult.State.Messages.Count);
        Assert.Equal("Conversation compacted", continueResult.State.Messages[0].Content);
        Assert.Equal("summary", continueResult.State.Messages[1].Content);
        Assert.Equal("kept tail", continueResult.State.Messages[2].Content);
        Assert.Equal(string.Empty, continueResult.State.Messages[3].Content);
        Assert.Equal("hook-result", continueResult.State.Messages[4].Content);
        Assert.Equal(6, emittedEvents.OfType<QueryMessageRuntimeEvent>().Count());
    }

    [Fact]
    public async Task RunAsync_Returns_AbortedStreaming_And_Emits_Interruption_Message_For_NonInterrupt_Cancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        var runner = new ModelBackedIterationRunner(
            new PostSamplingHookRegistry(),
            new QueryModelIterationRequestBuilder(),
            new CancellingModelCallExecutor(cancellationSource));
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-backed-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-model-15", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var emittedEvents = new List<QueryRuntimeEvent>();

        var result = await runner.RunAsync(
            QueryTurnRequest.Create(session, "hello") with
            {
                AbortReason = QueryAbortReason.Cancellation
            },
            QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")]),
            session,
            new ClawSharpSettings(),
            (runtimeEvent, _) =>
            {
                emittedEvents.Add(runtimeEvent);
                return Task.CompletedTask;
            },
            cancellationSource.Token);

        var terminal = Assert.IsType<QueryTerminalIterationResult>(result);
        Assert.Equal(QueryTerminalReason.AbortedStreaming, terminal.Terminal.Reason);
        var interruption = Assert.IsType<QueryMessageRuntimeEvent>(Assert.Single(emittedEvents));
        Assert.Equal(ChatMessageFactory.InterruptMessage, interruption.Message.Content);
    }

    [Fact]
    public async Task RunAsync_Returns_AbortedStreaming_Without_Interruption_Message_For_Interrupt_Cancellation()
    {
        using var cancellationSource = new CancellationTokenSource();
        var runner = new ModelBackedIterationRunner(
            new PostSamplingHookRegistry(),
            new QueryModelIterationRequestBuilder(),
            new CancellingModelCallExecutor(cancellationSource));
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-backed-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-model-16", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var emittedEvents = new List<QueryRuntimeEvent>();

        var result = await runner.RunAsync(
            QueryTurnRequest.Create(session, "hello") with
            {
                AbortReason = QueryAbortReason.Interrupt
            },
            QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")]),
            session,
            new ClawSharpSettings(),
            (runtimeEvent, _) =>
            {
                emittedEvents.Add(runtimeEvent);
                return Task.CompletedTask;
            },
            cancellationSource.Token);

        var terminal = Assert.IsType<QueryTerminalIterationResult>(result);
        Assert.Equal(QueryTerminalReason.AbortedStreaming, terminal.Terminal.Reason);
        Assert.Empty(emittedEvents);
    }

    [Fact]
    public async Task RunAsync_Maps_Generic_Model_Exception_To_ModelError_Terminal_And_Assistant_Api_Error_Message()
    {
        var runner = new ModelBackedIterationRunner(
            new PostSamplingHookRegistry(),
            new QueryModelIterationRequestBuilder(),
            new ThrowingModelCallExecutor(new InvalidOperationException("stream exploded")));
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-backed-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-model-17", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var emittedEvents = new List<QueryRuntimeEvent>();

        var result = await runner.RunAsync(
            QueryTurnRequest.Create(session, "hello"),
            QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")]),
            session,
            new ClawSharpSettings(),
            (runtimeEvent, _) =>
            {
                emittedEvents.Add(runtimeEvent);
                return Task.CompletedTask;
            });

        var terminal = Assert.IsType<QueryTerminalIterationResult>(result);
        Assert.Equal(QueryTerminalReason.ModelError, terminal.Terminal.Reason);
        Assert.Equal("stream exploded", terminal.Terminal.ErrorMessage);
        var messageEvent = Assert.IsType<QueryMessageRuntimeEvent>(Assert.Single(emittedEvents));
        Assert.Equal(MessageRole.Assistant, messageEvent.Message.Role);
        Assert.Equal("stream exploded", messageEvent.Message.Content);
        Assert.Equal("stream exploded", terminal.State.Messages[^1].Content);
    }

    [Fact]
    public async Task RunAsync_Dispatches_PostSampling_Hooks_With_Full_Completed_Context()
    {
        var postSamplingRegistry = new RecordingPostSamplingHookRegistry();
        var runner = new ModelBackedIterationRunner(
            postSamplingRegistry,
            new QueryModelIterationRequestBuilder(),
            new CompletedAssistantMessageModelCallExecutor());
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-backed-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-model-18", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
        var request = QueryTurnRequest.Create(session, "hello") with
        {
            ModelTurnContext = new QueryModelTurnContext(
                ["system prefix", "system dynamic"],
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["cwd"] = sessionRoot
                },
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["platform"] = "windows"
                },
                "repl_main_thread:post_sampling")
        };
        var priorUserMessage = ChatMessageFactory.CreateText(MessageRole.User, "hello");

        var result = await runner.RunAsync(
            request,
            QueryLoopStateFactory.CreateInitial([priorUserMessage], QueryToolUseContextState.Empty with { MainLoopModel = "claude-3-7-sonnet" }),
            session,
            new ClawSharpSettings(),
            static (_, _) => Task.CompletedTask);

        Assert.IsType<QueryTerminalIterationResult>(result);
        var hookContext = Assert.Single(postSamplingRegistry.Contexts);
        Assert.Equal("repl_main_thread:post_sampling", hookContext.QuerySource);
        Assert.Equal(["system prefix", "system dynamic"], hookContext.SystemPrompt);
        Assert.Equal(sessionRoot, hookContext.UserContext["cwd"]);
        Assert.Equal("windows", hookContext.SystemContext["platform"]);
        Assert.Equal("claude-3-7-sonnet", hookContext.ToolUseContext.MainLoopModel);
        Assert.Equal(2, hookContext.Messages.Count);
        Assert.Equal("hello", hookContext.Messages[0].Content);
        Assert.Equal("assistant completion", hookContext.Messages[1].Content);
    }

    [Fact]
    public async Task RunAsync_Does_Not_Dispatch_PostSampling_Hooks_Without_New_Assistant_Message()
    {
        var postSamplingRegistry = new RecordingPostSamplingHookRegistry();
        var runner = new ModelBackedIterationRunner(
            postSamplingRegistry,
            new QueryModelIterationRequestBuilder(),
            new CompletedWithoutAssistantModelCallExecutor());
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-backed-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-model-19", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));

        var result = await runner.RunAsync(
            QueryTurnRequest.Create(session, "hello"),
            QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")]),
            session,
            new ClawSharpSettings(),
            static (_, _) => Task.CompletedTask);

        Assert.IsType<QueryTerminalIterationResult>(result);
        Assert.Empty(postSamplingRegistry.Contexts);
    }

    private sealed class FakeIterationRequestBuilder : IQueryModelIterationRequestBuilder
    {
        public QueryModelHttpStreamingRequest Build(
            QueryTurnRequest request,
            QueryLoopState state,
            ClawSharpSettings settings)
        {
            var firstMessageText =
                state.Messages.FirstOrDefault()?.ContentBlocks.FirstOrDefault()?.Value ??
                request.UserInput;
            var firstSystemPrompt =
                request.ModelTurnContext?.SystemPrompt.FirstOrDefault() ??
                string.Empty;
            return new QueryModelHttpStreamingRequest(
                new QueryModelRequest(
                    request.SessionId,
                    "built-from-state",
                    [
                        new QuerySystemPromptBlock(firstSystemPrompt)
                    ],
                    [
                        new QueryRequestMessage(
                            "user",
                            [
                                new QueryRequestContentBlock("text", Text: firstMessageText)
                            ])
                    ],
                    [],
                    new QueryRequestOutputConfig(),
                    []),
                request.ModelTurnContext?.QuerySource ?? "repl_main_thread");
        }
    }

    private sealed class FakeCompletedModelCallExecutor : IQueryModelCallExecutor
    {
        public QueryModelHttpStreamingRequest? CapturedRequest { get; private set; }

        public async IAsyncEnumerable<QueryModelCallUpdate> StreamAsync(
            QueryModelHttpStreamingRequest streamingRequest,
            QueryTurnRequest request,
            QueryLoopState state,
            ConversationSession session,
            ClawSharpSettings settings,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var assistantMessage = ChatMessageFactory.CreateText(MessageRole.Assistant, "model-complete");
            var completedState = state with
            {
                Messages = state.Messages.Concat([assistantMessage]).ToArray()
            };
            CapturedRequest = streamingRequest;
            yield return new QueryModelCallUpdate(
                new QueryStreamEventRuntimeEvent(
                    new QueryStreamEvent(
                        new JsonObject
                        {
                            ["type"] = "message_start"
                        })));
            yield return new QueryModelCallUpdate(new QueryStreamDeltaRuntimeEvent("model-"));
            yield return new QueryModelCallUpdate(
                new QueryMessageRuntimeEvent(assistantMessage));
            yield return new QueryModelCallUpdate(
                AttemptResult: QueryModelCallAttemptResult.Completed(
                    new QueryTerminalIterationResult(
                        new QueryLoopTerminal(QueryTerminalReason.Completed),
                        completedState)));
            await Task.CompletedTask;
        }
    }

    private sealed class CancellingModelCallExecutor : IQueryModelCallExecutor
    {
        private readonly CancellationTokenSource _cancellationSource;

        public CancellingModelCallExecutor(CancellationTokenSource cancellationSource)
        {
            _cancellationSource = cancellationSource;
        }

        public async IAsyncEnumerable<QueryModelCallUpdate> StreamAsync(
            QueryModelHttpStreamingRequest streamingRequest,
            QueryTurnRequest request,
            QueryLoopState state,
            ConversationSession session,
            ClawSharpSettings settings,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            _cancellationSource.Cancel();
            await Task.Delay(1, cancellationToken);
            yield break;
        }
    }

    private sealed class ThrowingModelCallExecutor : IQueryModelCallExecutor
    {
        private readonly Exception _exception;

        public ThrowingModelCallExecutor(Exception exception)
        {
            _exception = exception;
        }

        public async IAsyncEnumerable<QueryModelCallUpdate> StreamAsync(
            QueryModelHttpStreamingRequest streamingRequest,
            QueryTurnRequest request,
            QueryLoopState state,
            ConversationSession session,
            ClawSharpSettings settings,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            throw _exception;
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }

    private sealed class CapturingModelCallExecutor : IQueryModelCallExecutor
    {
        public QueryModelHttpStreamingRequest? CapturedRequest { get; private set; }

        public async IAsyncEnumerable<QueryModelCallUpdate> StreamAsync(
            QueryModelHttpStreamingRequest streamingRequest,
            QueryTurnRequest request,
            QueryLoopState state,
            ConversationSession session,
            ClawSharpSettings settings,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            CapturedRequest = streamingRequest;
            yield return new QueryModelCallUpdate(
                AttemptResult: QueryModelCallAttemptResult.Completed(
                    new QueryTerminalIterationResult(
                        new QueryLoopTerminal(QueryTerminalReason.Completed),
                        state)));
            await Task.CompletedTask;
        }
    }

    private sealed class FallbackThenCompleteModelCallExecutor : IQueryModelCallExecutor
    {
        public List<QueryModelHttpStreamingRequest> CapturedRequests { get; } = [];
        private int _attempt;

        public async IAsyncEnumerable<QueryModelCallUpdate> StreamAsync(
            QueryModelHttpStreamingRequest streamingRequest,
            QueryTurnRequest request,
            QueryLoopState state,
            ConversationSession session,
            ClawSharpSettings settings,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            CapturedRequests.Add(streamingRequest);
            _attempt++;
            if (_attempt == 1)
            {
                yield return new QueryModelCallUpdate(
                    AttemptResult: QueryModelCallAttemptResult.FallbackRequested(
                        "primary-model",
                        "fallback-model"));
                yield break;
            }

            yield return new QueryModelCallUpdate(
                AttemptResult: QueryModelCallAttemptResult.Completed(
                    new QueryTerminalIterationResult(
                        new QueryLoopTerminal(QueryTerminalReason.Completed),
                        state)));
            await Task.CompletedTask;
        }
    }

    private sealed class CompletedToolUseModelCallExecutor : IQueryModelCallExecutor
    {
        public async IAsyncEnumerable<QueryModelCallUpdate> StreamAsync(
            QueryModelHttpStreamingRequest streamingRequest,
            QueryTurnRequest request,
            QueryLoopState state,
            ConversationSession session,
            ClawSharpSettings settings,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var assistantMessage = ChatMessageFactory.CreateToolUse(
                [("tooluse-model-1", "ModelTool", "{\"path\":\"note.txt\"}")]);
            var completedState = state with
            {
                Messages = state.Messages.Concat([assistantMessage]).ToArray()
            };
            yield return new QueryModelCallUpdate(new QueryMessageRuntimeEvent(assistantMessage));
            yield return new QueryModelCallUpdate(
                AttemptResult: QueryModelCallAttemptResult.Completed(
                    new QueryTerminalIterationResult(
                        new QueryLoopTerminal(QueryTerminalReason.Completed),
                        completedState)));
            await Task.CompletedTask;
        }
    }

    private sealed class WithheldMaxOutputTokensModelCallExecutor : IQueryModelCallExecutor
    {
        public async IAsyncEnumerable<QueryModelCallUpdate> StreamAsync(
            QueryModelHttpStreamingRequest streamingRequest,
            QueryTurnRequest request,
            QueryLoopState state,
            ConversationSession session,
            ClawSharpSettings settings,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var errorMessage = ChatMessageFactory.CreateAssistantApiErrorMessage(
                "max tokens",
                apiError: "max_output_tokens",
                error: "max_output_tokens");
            var completedState = state with
            {
                Messages = state.Messages.Concat([errorMessage]).ToArray()
            };
            yield return new QueryModelCallUpdate(
                AttemptResult: QueryModelCallAttemptResult.Completed(
                    new QueryTerminalIterationResult(
                        new QueryLoopTerminal(QueryTerminalReason.Completed),
                        completedState)));
            await Task.CompletedTask;
        }
    }

    private sealed class CompletedTurnOutputModelCallExecutor : IQueryModelCallExecutor
    {
        public async IAsyncEnumerable<QueryModelCallUpdate> StreamAsync(
            QueryModelHttpStreamingRequest streamingRequest,
            QueryTurnRequest request,
            QueryLoopState state,
            ConversationSession session,
            ClawSharpSettings settings,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var assistantMessage = ChatMessageFactory.CreateText(MessageRole.Assistant, "partial completion");
            var completedState = state with
            {
                Messages = state.Messages.Concat([assistantMessage]).ToArray()
            };
            yield return new QueryModelCallUpdate(
                new QueryMessageRuntimeEvent(assistantMessage));
            yield return new QueryModelCallUpdate(
                AttemptResult: QueryModelCallAttemptResult.Completed(
                    new QueryTerminalIterationResult(
                        new QueryLoopTerminal(QueryTerminalReason.Completed),
                        completedState),
                    turnOutputTokens: 400));
            await Task.CompletedTask;
        }
    }

    private sealed class CompletedAssistantMessageModelCallExecutor : IQueryModelCallExecutor
    {
        public async IAsyncEnumerable<QueryModelCallUpdate> StreamAsync(
            QueryModelHttpStreamingRequest streamingRequest,
            QueryTurnRequest request,
            QueryLoopState state,
            ConversationSession session,
            ClawSharpSettings settings,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var assistantMessage = ChatMessageFactory.CreateText(MessageRole.Assistant, "assistant completion");
            var completedState = state with
            {
                Messages = state.Messages.Concat([assistantMessage]).ToArray()
            };
            yield return new QueryModelCallUpdate(
                new QueryMessageRuntimeEvent(assistantMessage));
            yield return new QueryModelCallUpdate(
                AttemptResult: QueryModelCallAttemptResult.Completed(
                    new QueryTerminalIterationResult(
                        new QueryLoopTerminal(QueryTerminalReason.Completed),
                        completedState)));
            await Task.CompletedTask;
        }
    }

    private sealed class CompletedWithoutAssistantModelCallExecutor : IQueryModelCallExecutor
    {
        public async IAsyncEnumerable<QueryModelCallUpdate> StreamAsync(
            QueryModelHttpStreamingRequest streamingRequest,
            QueryTurnRequest request,
            QueryLoopState state,
            ConversationSession session,
            ClawSharpSettings settings,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new QueryModelCallUpdate(
                AttemptResult: QueryModelCallAttemptResult.Completed(
                    new QueryTerminalIterationResult(
                        new QueryLoopTerminal(QueryTerminalReason.Completed),
                        state)));
            await Task.CompletedTask;
        }
    }

    private sealed class PromptTooLongModelCallExecutor : IQueryModelCallExecutor
    {
        public async IAsyncEnumerable<QueryModelCallUpdate> StreamAsync(
            QueryModelHttpStreamingRequest streamingRequest,
            QueryTurnRequest request,
            QueryLoopState state,
            ConversationSession session,
            ClawSharpSettings settings,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var errorMessage = ChatMessageFactory.CreateAssistantApiErrorMessage(
                "Prompt is too long",
                apiError: "invalid_request",
                error: "invalid_request",
                errorDetails: "prompt is too long: 137500 tokens > 135000 maximum");
            var completedState = state with
            {
                Messages = state.Messages.Concat([errorMessage]).ToArray()
            };
            yield return new QueryModelCallUpdate(
                new QueryMessageRuntimeEvent(errorMessage));
            yield return new QueryModelCallUpdate(
                AttemptResult: QueryModelCallAttemptResult.Completed(
                    new QueryTerminalIterationResult(
                        new QueryLoopTerminal(QueryTerminalReason.Completed),
                        completedState)));
            await Task.CompletedTask;
        }
    }

    private sealed class MediaErrorModelCallExecutor : IQueryModelCallExecutor
    {
        public async IAsyncEnumerable<QueryModelCallUpdate> StreamAsync(
            QueryModelHttpStreamingRequest streamingRequest,
            QueryTurnRequest request,
            QueryLoopState state,
            ConversationSession session,
            ClawSharpSettings settings,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var errorMessage = ChatMessageFactory.CreateAssistantApiErrorMessage(
                "Image was too large",
                apiError: "invalid_request",
                error: "invalid_request",
                errorDetails: "image exceeds 5 MB maximum");
            var completedState = state with
            {
                Messages = state.Messages.Concat([errorMessage]).ToArray()
            };
            yield return new QueryModelCallUpdate(
                new QueryMessageRuntimeEvent(errorMessage));
            yield return new QueryModelCallUpdate(
                AttemptResult: QueryModelCallAttemptResult.Completed(
                    new QueryTerminalIterationResult(
                        new QueryLoopTerminal(QueryTerminalReason.Completed),
                        completedState)));
            await Task.CompletedTask;
        }
    }

    private sealed class CollapseDrainPromptOverflowRecoveryRunner : IQueryPromptOverflowRecoveryRunner
    {
        public Task<QueryContinueIterationResult?> TryRecoverAsync(
            QueryTurnRequest request,
            QueryLoopState priorState,
            QueryTerminalIterationResult terminalResult,
            ConversationSession session,
            ClawSharpSettings settings,
            Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<QueryContinueIterationResult?>(
                new QueryContinueIterationResult(
                    new QueryLoopTransition(QueryContinueReason.CollapseDrainRetry, Committed: 2),
                    request,
                    priorState with
                    {
                        Messages = terminalResult.State.Messages,
                        Transition = new QueryLoopTransition(QueryContinueReason.CollapseDrainRetry, Committed: 2)
                    }));
        }
    }

    private sealed class StaticReactiveCompactPromptOverflowRecoveryRunner : IQueryPromptOverflowRecoveryRunner
    {
        public Task<QueryContinueIterationResult?> TryRecoverAsync(
            QueryTurnRequest request,
            QueryLoopState priorState,
            QueryTerminalIterationResult terminalResult,
            ConversationSession session,
            ClawSharpSettings settings,
            Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<QueryContinueIterationResult?>(
                new QueryContinueIterationResult(
                    new QueryLoopTransition(QueryContinueReason.ReactiveCompactRetry),
                    request,
                    priorState with
                    {
                        Messages = terminalResult.State.Messages,
                        HasAttemptedReactiveCompact = true,
                        Transition = new QueryLoopTransition(QueryContinueReason.ReactiveCompactRetry)
                    }));
        }
    }

    private sealed class FakeReactiveCompactExecutor : IQueryReactiveCompactExecutor
    {
        public Task<QueryCompactionResult?> TryReactiveCompactAsync(
            QueryTurnRequest request,
            QueryLoopState priorState,
            QueryTerminalIterationResult terminalResult,
            ConversationSession session,
            ClawSharpSettings settings,
            CancellationToken cancellationToken = default)
        {
            var boundary = ChatMessageFactory.CreateCompactBoundaryMessage("auto", 500);
            var summary = ChatMessageFactory.CreateText(MessageRole.User, "summary");
            var keptTail = ChatMessageFactory.CreateText(MessageRole.User, "kept tail");
            var attachment = ChatMessageFactory.CreateOutputTokenUsageAttachmentMessage(50, 50, 100);
            var hookResult = ChatMessageFactory.CreateText(MessageRole.System, "hook-result");

            return Task.FromResult<QueryCompactionResult?>(
                new QueryCompactionResult(
                    boundary,
                    [summary],
                    [attachment],
                    [hookResult],
                    [keptTail],
                    PreCompactTokenCount: 500,
                    PostCompactTokenCount: 100));
        }
    }

    private sealed class RecordingPostSamplingHookRegistry : IPostSamplingHookRegistry
    {
        public List<ReplHookContext> Contexts { get; } = [];

        public void Register(PostSamplingHook hook)
        {
            throw new NotSupportedException();
        }

        public void Clear()
        {
            Contexts.Clear();
        }

        public Task ExecuteAsync(ReplHookContext context)
        {
            Contexts.Add(context);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingToolUseSummaryGenerator : IQueryToolUseSummaryGenerator
    {
        public QueryToolUseSummaryGenerationRequest? LastRequest { get; private set; }

        public Task<QueryToolUseSummaryMessage?> GenerateAsync(
            QueryToolUseSummaryGenerationRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult<QueryToolUseSummaryMessage?>(
                QueryToolUseSummaryMessageFactory.Create(
                    $"summary:{request.Tools[0].Output}",
                    request.Tools.Select(tool => tool.ToolUseId).ToArray()));
        }
    }

    private sealed class RecordingStopHookRunner : IQueryStopHookRunner
    {
        private readonly QueryStopHookExecutionResult _result;

        public RecordingStopHookRunner(QueryStopHookExecutionResult result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public async Task<QueryStopHookExecutionResult> RunAsync(
            QueryTurnRequest request,
            QueryLoopState state,
            ConversationSession session,
            ClawSharpSettings settings,
            Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            foreach (var message in _result.BlockingMessages)
            {
                await emitEvent(new QueryMessageRuntimeEvent(message), cancellationToken);
            }

            return _result;
        }
    }

    private sealed class ModelBackedToolUseTestTool : IClawSharpTool
    {
        public ModelBackedToolUseTestTool()
        {
            Descriptor = new ToolDescriptor("ModelTool", "Tool used by the model-backed next-turn continuation test.");
        }

        public ToolDescriptor Descriptor { get; }

        public bool IsEnabled() => true;

        public bool IsConcurrencySafe(string arguments) => false;

        public bool IsReadOnly(string arguments) => true;

        public string? RenderToolUseProgressMessage(IReadOnlyList<ToolProgressUpdate> progressMessages) => null;

        public string? RenderToolResultMessage(string content, JsonNode? structuredOutput, IReadOnlyList<ToolProgressUpdate> progressMessages) => null;

        public Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ToolValidationResult.Valid());
        }

        public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ToolExecutionResult(true, "model tool output"));
        }
    }
}
