// TS origin: ./query.ts, ./utils/api.ts, ./services/api/claude.ts
// TS parity status: ports the live model-backed query-loop iteration path for streamed transport, fallback, max-output-token recovery, post-tool continuation, stop-hook ordering, and token-budget continuation; some recursive recovery and compaction branches remain intentionally unported.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed class ModelBackedIterationRunner : IQueryIterationRunner
{
    private readonly IPostSamplingHookRegistry _postSamplingHookRegistry;
    private readonly IQueryModelIterationRequestBuilder _iterationRequestBuilder;
    private readonly IQueryModelCallExecutor _modelCallExecutor;
    private readonly IQueryPromptOverflowRecoveryRunner _promptOverflowRecoveryRunner;
    private readonly ToolOrchestrator? _toolOrchestrator;
    private readonly IQueryStopHookRunner? _stopHookRunner;
    private readonly IQueryToolUseSummaryGenerator _toolUseSummaryGenerator;

    public ModelBackedIterationRunner(
        IPostSamplingHookRegistry? postSamplingHookRegistry = null,
        IQueryModelIterationRequestBuilder? iterationRequestBuilder = null,
        IQueryModelCallExecutor? modelCallExecutor = null,
        IQueryPromptOverflowRecoveryRunner? promptOverflowRecoveryRunner = null,
        ToolOrchestrator? toolOrchestrator = null,
        IQueryStopHookRunner? stopHookRunner = null,
        IQueryToolUseSummaryGenerator? toolUseSummaryGenerator = null)
    {
        _postSamplingHookRegistry = postSamplingHookRegistry ?? new PostSamplingHookRegistry();
        _iterationRequestBuilder = iterationRequestBuilder ?? new QueryModelIterationRequestBuilder();
        _modelCallExecutor = modelCallExecutor ?? new NotImplementedQueryModelCallExecutor();
        _promptOverflowRecoveryRunner = promptOverflowRecoveryRunner ?? new NoOpQueryPromptOverflowRecoveryRunner();
        _toolOrchestrator = toolOrchestrator;
        _stopHookRunner = stopHookRunner;
        _toolUseSummaryGenerator = toolUseSummaryGenerator ?? new NoOpQueryToolUseSummaryGenerator();
    }

    internal Task ExecutePostSamplingHooksAsync(ReplHookContext context)
    {
        return _postSamplingHookRegistry.ExecuteAsync(context);
    }

    public async Task<QueryIterationResult> RunAsync(
        QueryTurnRequest request,
        QueryLoopState state,
        ConversationSession session,
        ClawSharpSettings settings,
        Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
        CancellationToken cancellationToken = default)
    {
        var currentState = state;

        while (true)
        {
            QueryModelCallAttemptResult? attemptResult = null;
            var streamingRequest = _iterationRequestBuilder.Build(request, currentState, settings);

            try
            {
                await foreach (var update in _modelCallExecutor.StreamAsync(
                                   streamingRequest,
                                   request,
                                   currentState,
                                   session,
                                   settings,
                                   cancellationToken))
                {
                    if (update.RuntimeEvent is not null)
                    {
                        await emitEvent(update.RuntimeEvent, cancellationToken);
                    }

                    if (update.AttemptResult is not null)
                    {
                        attemptResult = update.AttemptResult;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (request.AbortReason != QueryAbortReason.Interrupt)
                {
                    await emitEvent(
                        new QueryMessageRuntimeEvent(ChatMessageFactory.CreateUserInterruptionMessage(toolUse: false)),
                        CancellationToken.None);
                }

                return new QueryTerminalIterationResult(
                    new QueryLoopTerminal(QueryTerminalReason.AbortedStreaming),
                    currentState);
            }
            catch (QueryExecutionNotImplementedException)
            {
                throw;
            }
            catch (Exception exception)
            {
                var errorMessage = ChatMessageFactory.CreateAssistantApiErrorMessage(exception.Message);
                await emitEvent(new QueryMessageRuntimeEvent(errorMessage), CancellationToken.None);

                return new QueryTerminalIterationResult(
                    new QueryLoopTerminal(
                        QueryTerminalReason.ModelError,
                        ErrorMessage: exception.Message),
                    currentState with
                    {
                        Messages = currentState.Messages.Concat([errorMessage]).ToArray()
                    });
            }

            switch (attemptResult?.Outcome)
            {
                case QueryModelCallAttemptOutcome.Completed when attemptResult.IterationResult is not null:
                    return await HandleCompletedAttemptAsync(
                        request,
                        currentState,
                        attemptResult,
                        session,
                        settings,
                        emitEvent,
                        cancellationToken);
                case QueryModelCallAttemptOutcome.FallbackRequested
                    when !string.IsNullOrWhiteSpace(attemptResult.OriginalModel) &&
                         !string.IsNullOrWhiteSpace(attemptResult.FallbackModel):
                    currentState = currentState with
                    {
                        ToolUseContext = currentState.ToolUseContext with
                        {
                            MainLoopModel = attemptResult.FallbackModel
                        }
                    };
                    await emitEvent(
                        new QueryMessageRuntimeEvent(
                            ChatMessageFactory.CreateSystemMessage(
                                $"Switched to {attemptResult.FallbackModel} due to high demand for {attemptResult.OriginalModel}",
                                "warning")),
                        cancellationToken);
                    continue;
                default:
                    throw new QueryExecutionNotImplementedException();
            }
        }
    }

    private async Task<QueryIterationResult> HandleCompletedAttemptAsync(
        QueryTurnRequest request,
        QueryLoopState priorState,
        QueryModelCallAttemptResult completedAttempt,
        ConversationSession session,
        ClawSharpSettings settings,
        Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
        CancellationToken cancellationToken)
    {
        if (completedAttempt.IterationResult is not QueryTerminalIterationResult terminalResult)
        {
            return completedAttempt.IterationResult!;
        }

        var completedState = terminalResult.State with
        {
            PendingToolUseSummary = null
        };

        TryDispatchPostSamplingHooks(request, priorState, completedState);
        await EmitPendingToolUseSummaryAsync(priorState, emitEvent, cancellationToken);

        if (TryGetNewToolCalls(completedState.Messages, priorState.Messages.Count, out var toolCalls))
        {
            return await ContinueWithToolResultsAsync(
                request,
                priorState,
                completedState,
                toolCalls,
                session,
                settings,
                emitEvent,
                cancellationToken);
        }

        var terminalAfterPendingSummary = terminalResult with
        {
            State = completedState
        };

        if (!TryGetWithheldMaxOutputTokensMessage(completedState, priorState.Messages.Count, out var withheldMessage))
        {
            return await HandleCompletedAttemptWithoutWithheldMaxTokensAsync(
                request,
                priorState,
                terminalAfterPendingSummary,
                completedAttempt.TurnOutputTokens,
                session,
                settings,
                emitEvent,
                cancellationToken);
        }

        var recoveryDecision = QueryMaxOutputTokensRecoveryPolicy.Evaluate(
            capEnabled: true,
            isWithheldMaxOutputTokens: true,
            recoveryCount: priorState.MaxOutputTokensRecoveryCount,
            maxOutputTokensOverride: priorState.MaxOutputTokensOverride,
            hasEnvironmentMaxOutputTokensOverride: !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("CLAUDE_CODE_MAX_OUTPUT_TOKENS")));

        if (recoveryDecision is not null)
        {
            if (recoveryDecision.Transition.Reason == QueryContinueReason.MaxOutputTokensEscalate)
            {
                return new QueryContinueIterationResult(
                    recoveryDecision.Transition,
                    request,
                    priorState with
                    {
                        MaxOutputTokensOverride = recoveryDecision.NextMaxOutputTokensOverride,
                        MaxOutputTokensRecoveryCount = recoveryDecision.NextRecoveryCount,
                        Transition = recoveryDecision.Transition
                    });
            }

            if (recoveryDecision.RecoveryMessageContent is not null)
            {
                var recoveryMessage = ChatMessageFactory.CreateUserMessage(
                    recoveryDecision.RecoveryMessageContent,
                    isMeta: true);

                return new QueryContinueIterationResult(
                    recoveryDecision.Transition,
                    request,
                    priorState with
                    {
                        Messages = terminalResult.State.Messages.Concat([recoveryMessage]).ToArray(),
                        PendingToolUseSummary = null,
                        MaxOutputTokensOverride = recoveryDecision.NextMaxOutputTokensOverride,
                        MaxOutputTokensRecoveryCount = recoveryDecision.NextRecoveryCount,
                        Transition = recoveryDecision.Transition
                    });
            }
        }

        await emitEvent(new QueryMessageRuntimeEvent(withheldMessage), cancellationToken);
        return terminalAfterPendingSummary;
    }

    private async Task<QueryIterationResult> HandleCompletedAttemptWithoutWithheldMaxTokensAsync(
        QueryTurnRequest request,
        QueryLoopState priorState,
        QueryTerminalIterationResult terminalResult,
        int? turnOutputTokens,
        ConversationSession session,
        ClawSharpSettings settings,
        Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
        CancellationToken cancellationToken)
    {
        var budget = request.TaskBudget?.Remaining ?? request.TaskBudget?.Total;
        var stateWithUsage = terminalResult.State;
        if (budget is not null && budget > 0 && turnOutputTokens is not null && turnOutputTokens > 0)
        {
            var sessionOutputTokens = GetPriorSessionOutputTokens(priorState.Messages) + turnOutputTokens.Value;
            var usageAttachment = ChatMessageFactory.CreateOutputTokenUsageAttachmentMessage(
                turnOutputTokens.Value,
                sessionOutputTokens,
                budget.Value);
            await emitEvent(new QueryMessageRuntimeEvent(usageAttachment), cancellationToken);

            stateWithUsage = stateWithUsage with
            {
                Messages = stateWithUsage.Messages.Concat([usageAttachment]).ToArray()
            };
        }

        var terminalWithUsage = terminalResult with
        {
            State = stateWithUsage
        };

        if (IsAssistantApiErrorMessage(stateWithUsage.Messages.LastOrDefault()))
        {
            return await ApplyApiErrorTerminalClassificationAsync(
                request,
                priorState,
                terminalWithUsage,
                session,
                settings,
                emitEvent,
                cancellationToken);
        }

        var postStopHookState = await TryApplyStopHooksAsync(
            request,
            priorState,
            stateWithUsage,
            session,
            settings,
            emitEvent,
            cancellationToken);
        if (postStopHookState.Result is not null)
        {
            return postStopHookState.Result;
        }

        stateWithUsage = postStopHookState.State!;

        if (budget is null || budget <= 0 || turnOutputTokens is null || turnOutputTokens <= 0)
        {
            return terminalWithUsage with
            {
                State = stateWithUsage
            };
        }

        var tracker = priorState.TokenBudgetTracker ?? QueryTokenBudgetTracker.Create();
        var decision = QueryTokenBudgetPolicy.Evaluate(tracker, budget, turnOutputTokens.Value);
        if (decision is not QueryTokenBudgetContinueDecision continueDecision)
        {
            return terminalWithUsage with
            {
                State = stateWithUsage with
                {
                    TokenBudgetTracker = decision.Tracker
                }
            };
        }

        var continuationMessage = ChatMessageFactory.CreateUserMessage(
            continueDecision.NudgeMessage,
            isMeta: true);
        var nextTaskBudget = CreateNextTaskBudget(request.TaskBudget, budget.Value, turnOutputTokens.Value);

        return new QueryContinueIterationResult(
            new QueryLoopTransition(QueryContinueReason.TokenBudgetContinuation),
            request with
            {
                TaskBudget = nextTaskBudget
            },
            stateWithUsage with
            {
                Messages = stateWithUsage.Messages.Concat([continuationMessage]).ToArray(),
                TokenBudgetTracker = continueDecision.Tracker,
                MaxOutputTokensRecoveryCount = 0,
                HasAttemptedReactiveCompact = false,
                MaxOutputTokensOverride = null,
                PendingToolUseSummary = null,
                StopHookActive = null,
                Transition = new QueryLoopTransition(QueryContinueReason.TokenBudgetContinuation)
            });
    }

    private async Task<QueryIterationResult> ApplyApiErrorTerminalClassificationAsync(
        QueryTurnRequest request,
        QueryLoopState priorState,
        QueryTerminalIterationResult terminalResult,
        ConversationSession session,
        ClawSharpSettings settings,
        Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
        CancellationToken cancellationToken)
    {
        var classifiedTerminal = QueryApiErrorTerminalClassifier.Classify(
            terminalResult.State.Messages.LastOrDefault(),
            priorState);
        if (classifiedTerminal is null)
        {
            return terminalResult;
        }

        var classifiedResult = terminalResult with
        {
            Terminal = classifiedTerminal
        };

        if (classifiedTerminal.Reason is QueryTerminalReason.PromptTooLong or QueryTerminalReason.ImageError)
        {
            var recovered = await _promptOverflowRecoveryRunner.TryRecoverAsync(
                request,
                priorState,
                classifiedResult,
                session,
                settings,
                emitEvent,
                cancellationToken);
            if (recovered is not null)
            {
                return recovered;
            }
        }

        return classifiedResult;
    }

    private static bool TryGetWithheldMaxOutputTokensMessage(
        QueryLoopState completedState,
        int priorMessageCount,
        out ChatMessage message)
    {
        message = null!;

        if (completedState.Messages.Count <= priorMessageCount)
        {
            return false;
        }

        for (var index = completedState.Messages.Count - 1; index >= priorMessageCount; index--)
        {
            var candidate = completedState.Messages[index];
            if (IsMaxOutputTokensApiErrorMessage(candidate))
            {
                message = candidate;
                return true;
            }
        }

        return false;
    }

    private async Task<QueryIterationResult> ContinueWithToolResultsAsync(
        QueryTurnRequest request,
        QueryLoopState priorState,
        QueryLoopState completedState,
        IReadOnlyList<ToolCallRequest> toolCalls,
        ConversationSession session,
        ClawSharpSettings settings,
        Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
        CancellationToken cancellationToken)
    {
        if (_toolOrchestrator is null)
        {
            throw new QueryExecutionNotImplementedException();
        }

        var emittedMessages = completedState.Messages.ToList();
        var toolUseContext = completedState.ToolUseContext;
        var shouldPreventContinuation = false;

        async Task EmitAndTrackAsync(ChatMessage message, CancellationToken token)
        {
            emittedMessages.Add(message);
            await emitEvent(new QueryMessageRuntimeEvent(message), token);
        }

        try
        {
            await foreach (var update in _toolOrchestrator.StreamAsync(
                               toolCalls,
                               session,
                               settings,
                               querySource: request.ModelTurnContext?.QuerySource,
                               currentSystemPrompt: request.ModelTurnContext?.SystemPrompt,
                               cancellationToken: cancellationToken))
            {
                if (update.Message is not null)
                {
                    await EmitAndTrackAsync(update.Message, cancellationToken);
                    if (QueryAttachmentHelpers.TryGetHookStoppedContinuationAttachment(update.Message, out _))
                    {
                        shouldPreventContinuation = true;
                    }
                }

                if (update.Result is not null)
                {
                    if (update.UpdatedToolUseContext is not null)
                    {
                        toolUseContext = update.UpdatedToolUseContext;
                    }

                    var toolResultMessage = ChatMessageFactory.CreateToolResult(
                        update.Result.ToolCall.ToolUseId,
                        update.Result.ToolCall.ToolName,
                        update.Result.Output,
                        update.Result.StructuredOutput);
                    await EmitAndTrackAsync(toolResultMessage, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (request.AbortReason is not null || cancellationToken.IsCancellationRequested)
        {
            if (request.AbortReason != QueryAbortReason.Interrupt)
            {
                var interruptionMessage = ChatMessageFactory.CreateUserInterruptionMessage(toolUse: true);
                await EmitAndTrackAsync(interruptionMessage, CancellationToken.None);
            }

            var maxTurnsNotification = QueryMaxTurnsPolicy.Evaluate(request.MaxTurns, priorState.TurnCount + 1);
            if (maxTurnsNotification is not null)
            {
                var maxTurnsMessage = ChatMessageFactory.CreateMaxTurnsReachedAttachmentMessage(
                    maxTurnsNotification.MaxTurns,
                    maxTurnsNotification.TurnCount);
                await EmitAndTrackAsync(maxTurnsMessage, CancellationToken.None);
            }

            return new QueryTerminalIterationResult(
                new QueryLoopTerminal(QueryTerminalReason.AbortedTools),
                completedState with
                {
                    Messages = emittedMessages.ToArray(),
                    ToolUseContext = toolUseContext,
                    Transition = null
                });
        }

        if (shouldPreventContinuation)
        {
            return new QueryTerminalIterationResult(
                new QueryLoopTerminal(QueryTerminalReason.HookStopped),
                completedState with
                {
                    Messages = emittedMessages.ToArray(),
                    ToolUseContext = toolUseContext,
                    Transition = null
                });
        }

        var postToolState = completedState with
        {
            Messages = emittedMessages.ToArray(),
            ToolUseContext = toolUseContext,
            PendingToolUseSummary = null
        };

        var nextTurnCount = priorState.TurnCount + 1;
        var maxTurnsReached = QueryMaxTurnsPolicy.Evaluate(request.MaxTurns, nextTurnCount);
        if (maxTurnsReached is not null)
        {
            var maxTurnsMessage = ChatMessageFactory.CreateMaxTurnsReachedAttachmentMessage(
                maxTurnsReached.MaxTurns,
                maxTurnsReached.TurnCount);
            await EmitAndTrackAsync(maxTurnsMessage, cancellationToken);
            return new QueryTerminalIterationResult(
                new QueryLoopTerminal(
                    QueryTerminalReason.MaxTurns,
                    TurnCount: maxTurnsReached.TurnCount),
                postToolState with
                {
                    Messages = emittedMessages.ToArray(),
                    Transition = null
                });
        }

        return new QueryContinueIterationResult(
            new QueryLoopTransition(QueryContinueReason.NextTurn),
            request with
            {
                RequestedTools = [],
                ExecutionMode = QueryTurnExecutionMode.ModelBacked
            },
            postToolState with
            {
                TurnCount = nextTurnCount,
                MaxOutputTokensRecoveryCount = 0,
                HasAttemptedReactiveCompact = false,
                PendingToolUseSummary = BuildPendingToolUseSummaryTask(
                    toolCalls,
                    emittedMessages,
                    cancellationToken),
                MaxOutputTokensOverride = null,
                StopHookActive = null,
                Transition = new QueryLoopTransition(QueryContinueReason.NextTurn)
            });
    }

    private static bool TryGetNewToolCalls(
        IReadOnlyList<ChatMessage> messages,
        int priorMessageCount,
        out IReadOnlyList<ToolCallRequest> toolCalls)
    {
        List<ToolCallRequest> collected = [];

        for (var messageIndex = priorMessageCount; messageIndex < messages.Count; messageIndex++)
        {
            var message = messages[messageIndex];
            if (message.Role != MessageRole.Assistant)
            {
                continue;
            }

            foreach (var block in message.ContentBlocks.Where(block => block.Kind == MessageContentKind.ToolUse))
            {
                if (block.Metadata is null ||
                    !block.Metadata.TryGetValue("toolUseId", out var toolUseId) ||
                    string.IsNullOrWhiteSpace(toolUseId))
                {
                    continue;
                }

                collected.Add(
                    new ToolCallRequest(
                        toolUseId,
                        block.Name ?? string.Empty,
                        block.Value));
            }
        }

        toolCalls = collected;
        return collected.Count > 0;
    }

    private async Task EmitPendingToolUseSummaryAsync(
        QueryLoopState priorState,
        Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
        CancellationToken cancellationToken)
    {
        if (priorState.PendingToolUseSummary is null)
        {
            return;
        }

        QueryToolUseSummaryMessage? summary;
        try
        {
            summary = await priorState.PendingToolUseSummary;
        }
        catch
        {
            return;
        }

        if (summary is null)
        {
            return;
        }

        await emitEvent(new QueryToolUseSummaryRuntimeEvent(summary), cancellationToken);
    }

    private Task<QueryToolUseSummaryMessage?> BuildPendingToolUseSummaryTask(
        IReadOnlyList<ToolCallRequest> toolCalls,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var request = new QueryToolUseSummaryGenerationRequest(
            toolCalls.Select(
                toolCall => new QueryToolUseSummaryToolEntry(
                    toolCall.ToolUseId,
                    toolCall.ToolName,
                    toolCall.Arguments,
                    TryGetToolResultOutput(messages, toolCall.ToolUseId))).ToArray(),
            TryGetLastAssistantText(messages));

        return GeneratePendingToolUseSummaryAsync(request, cancellationToken);
    }

    private async Task<QueryToolUseSummaryMessage?> GeneratePendingToolUseSummaryAsync(
        QueryToolUseSummaryGenerationRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _toolUseSummaryGenerator.GenerateAsync(request, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetToolResultOutput(IReadOnlyList<ChatMessage> messages, string toolUseId)
    {
        for (var index = messages.Count - 1; index >= 0; index--)
        {
            var message = messages[index];
            if (message.Role != MessageRole.User)
            {
                continue;
            }

            var toolResultBlock = message.ContentBlocks.FirstOrDefault(
                block => block.Kind == MessageContentKind.ToolResult &&
                         block.Metadata is not null &&
                         block.Metadata.TryGetValue("toolUseId", out var candidateToolUseId) &&
                         string.Equals(candidateToolUseId, toolUseId, StringComparison.Ordinal));
            if (toolResultBlock is not null)
            {
                return toolResultBlock.Value;
            }
        }

        return null;
    }

    private static string? TryGetLastAssistantText(IReadOnlyList<ChatMessage> messages)
    {
        for (var index = messages.Count - 1; index >= 0; index--)
        {
            var message = messages[index];
            if (message.Role != MessageRole.Assistant)
            {
                continue;
            }

            var text = string.Join(
                "\n",
                message.ContentBlocks
                    .Where(block => block.Kind == MessageContentKind.Text)
                    .Select(block => block.Value)
                    .Where(content => !string.IsNullOrWhiteSpace(content)))
                .Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static bool IsMaxOutputTokensApiErrorMessage(ChatMessage message)
    {
        if (message.Role != MessageRole.Assistant)
        {
            return false;
        }

        var block = message.ContentBlocks.FirstOrDefault();
        return block?.Metadata is not null &&
               block.Metadata.TryGetValue("isApiErrorMessage", out var isApiError) &&
               string.Equals(isApiError, bool.TrueString, StringComparison.OrdinalIgnoreCase) &&
               block.Metadata.TryGetValue("apiError", out var apiError) &&
               string.Equals(apiError, "max_output_tokens", StringComparison.Ordinal);
    }

    private static int GetPriorSessionOutputTokens(IReadOnlyList<ChatMessage> messages)
    {
        for (var index = messages.Count - 1; index >= 0; index--)
        {
            if (QueryAttachmentHelpers.TryGetOutputTokenUsageAttachment(messages[index], out var attachment))
            {
                return attachment!.Session;
            }
        }

        return 0;
    }

    private async Task<(QueryLoopState? State, QueryIterationResult? Result)> TryApplyStopHooksAsync(
        QueryTurnRequest request,
        QueryLoopState priorState,
        QueryLoopState state,
        ConversationSession session,
        ClawSharpSettings settings,
        Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
        CancellationToken cancellationToken)
    {
        if (_stopHookRunner is null)
        {
            return (state, null);
        }

        var emittedMessages = state.Messages.ToList();
        var stopHookResult = await _stopHookRunner.RunAsync(
            request,
            state,
            session,
            settings,
            async (runtimeEvent, token) =>
            {
                if (runtimeEvent is QueryMessageRuntimeEvent messageEvent)
                {
                    emittedMessages.Add(messageEvent.Message);
                }

                await emitEvent(runtimeEvent, token);
            },
            cancellationToken);

        var postStopHookState = state with
        {
            Messages = emittedMessages.ToArray()
        };

        if (stopHookResult.PreventContinuation)
        {
            return (
                null,
                new QueryTerminalIterationResult(
                    new QueryLoopTerminal(QueryTerminalReason.StopHookPrevented),
                    postStopHookState with
                    {
                        Transition = null,
                        StopHookActive = true
                    }));
        }

        if (stopHookResult.BlockingMessages.Count > 0)
        {
            var transition = new QueryLoopTransition(QueryContinueReason.StopHookBlocking);
            return (
                null,
                new QueryContinueIterationResult(
                    transition,
                    request,
                    postStopHookState with
                    {
                        MaxOutputTokensRecoveryCount = 0,
                        MaxOutputTokensOverride = null,
                        PendingToolUseSummary = null,
                        StopHookActive = true,
                        Transition = transition
                    }));
        }

        return (postStopHookState, null);
    }

    private static QueryTaskBudget? CreateNextTaskBudget(
        QueryTaskBudget? currentTaskBudget,
        int budget,
        int turnOutputTokens)
    {
        if (currentTaskBudget is null)
        {
            return null;
        }

        return currentTaskBudget with
        {
            Remaining = Math.Max(0, budget - turnOutputTokens)
        };
    }

    private static bool IsAssistantApiErrorMessage(ChatMessage? message)
    {
        if (message?.Role != MessageRole.Assistant)
        {
            return false;
        }

        var block = message.ContentBlocks.FirstOrDefault();
        return block?.Metadata is not null &&
               block.Metadata.TryGetValue("isApiErrorMessage", out var isApiError) &&
               string.Equals(isApiError, bool.TrueString, StringComparison.OrdinalIgnoreCase);
    }

    private void TryDispatchPostSamplingHooks(
        QueryTurnRequest request,
        QueryLoopState priorState,
        QueryLoopState completedState)
    {
        if (!HasNewAssistantMessages(completedState.Messages, priorState.Messages.Count))
        {
            return;
        }

        var modelTurnContext = request.ModelTurnContext ?? QueryModelTurnContext.ReplMainThread;
        _ = ExecutePostSamplingHooksAsync(
            new ReplHookContext(
                completedState.Messages,
                modelTurnContext.SystemPrompt,
                modelTurnContext.UserContext,
                modelTurnContext.SystemContext,
                completedState.ToolUseContext,
                modelTurnContext.QuerySource));
    }

    private static bool HasNewAssistantMessages(IReadOnlyList<ChatMessage> messages, int priorMessageCount)
    {
        if (messages.Count <= priorMessageCount)
        {
            return false;
        }

        for (var index = priorMessageCount; index < messages.Count; index++)
        {
            if (messages[index].Role == MessageRole.Assistant)
            {
                return true;
            }
        }

        return false;
    }
}
