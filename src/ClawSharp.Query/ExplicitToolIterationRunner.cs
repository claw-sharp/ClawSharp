// TS origin: ./query.ts, ./services/tools/toolOrchestration.ts, ./services/tools/StreamingToolExecutor.ts
// TS parity status: ports the current explicit-tool single-iteration execution path under the new outer query-loop coordinator; multi-iteration model-backed continuation remains blocked.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed class ExplicitToolIterationRunner : IQueryIterationRunner
{
    private readonly ToolOrchestrator _toolOrchestrator;
    private readonly IQueryStopHookRunner? _stopHookRunner;

    public ExplicitToolIterationRunner(
        ToolOrchestrator toolOrchestrator,
        IQueryStopHookRunner? stopHookRunner = null)
    {
        _toolOrchestrator = toolOrchestrator;
        _stopHookRunner = stopHookRunner;
    }

    public async Task<QueryIterationResult> RunAsync(
        QueryTurnRequest request,
        QueryLoopState state,
        ConversationSession session,
        ClawSharpSettings settings,
        Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
        CancellationToken cancellationToken = default)
    {
        var toolUseMessage = ChatMessageFactory.CreateToolUse(
            request.RequestedTools
                .Select(toolCall => (toolCall.ToolUseId, toolCall.ToolName, toolCall.Arguments))
                .ToArray());
        await emitEvent(new QueryMessageRuntimeEvent(toolUseMessage), cancellationToken);

        var toolResults = new List<ToolExecutionRecord>();
        var shouldPreventContinuation = false;
        var toolUseContext = state.ToolUseContext;
        try
        {
            await foreach (var update in _toolOrchestrator.StreamAsync(
                request.RequestedTools,
                session,
                settings,
                (toolCall, progressUpdate) =>
                    emitEvent(
                        new QueryMessageRuntimeEvent(
                            ChatMessageFactory.CreateProgress(
                                progressUpdate.ToolUseId,
                                toolCall.ToolUseId,
                                progressUpdate.Data)),
                        cancellationToken).GetAwaiter().GetResult(),
                request.ModelTurnContext?.QuerySource,
                request.ModelTurnContext?.SystemPrompt,
                cancellationToken))
            {
                if (update.Message is not null)
                {
                    await emitEvent(new QueryMessageRuntimeEvent(update.Message), cancellationToken);
                    if (QueryAttachmentHelpers.TryGetHookStoppedContinuationAttachment(update.Message, out _))
                    {
                        shouldPreventContinuation = true;
                    }
                }

                if (update.Result is not null)
                {
                    toolResults.Add(update.Result);
                    if (update.UpdatedToolUseContext is not null)
                    {
                        toolUseContext = update.UpdatedToolUseContext;
                    }

                    await emitEvent(
                        new QueryMessageRuntimeEvent(
                            ChatMessageFactory.CreateToolResult(
                                update.Result.ToolCall.ToolUseId,
                                update.Result.ToolCall.ToolName,
                                update.Result.Output,
                                update.Result.StructuredOutput)),
                        cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (request.AbortReason is not null || cancellationToken.IsCancellationRequested)
        {
            if (request.AbortReason != QueryAbortReason.Interrupt)
            {
                await emitEvent(
                    new QueryMessageRuntimeEvent(ChatMessageFactory.CreateUserInterruptionMessage(toolUse: true)),
                    CancellationToken.None);
            }

            var maxTurnsNotification = QueryMaxTurnsPolicy.Evaluate(request.MaxTurns, state.TurnCount + 1);
            if (maxTurnsNotification is not null)
            {
                await emitEvent(
                    new QueryMessageRuntimeEvent(
                        ChatMessageFactory.CreateMaxTurnsReachedAttachmentMessage(
                            maxTurnsNotification.MaxTurns,
                            maxTurnsNotification.TurnCount)),
                    CancellationToken.None);
            }

            return new QueryTerminalIterationResult(
                new QueryLoopTerminal(QueryTerminalReason.AbortedTools),
                state with
                {
                    Messages = session.Messages.ToArray(),
                    ToolUseContext = toolUseContext,
                    Transition = null
                });
        }

        if (shouldPreventContinuation)
        {
            return new QueryTerminalIterationResult(
                new QueryLoopTerminal(QueryTerminalReason.HookStopped),
                state with
                {
                    Messages = session.Messages.ToArray(),
                    ToolUseContext = toolUseContext,
                    Transition = null
                });
        }

        if (_stopHookRunner is not null)
        {
            var stopHookResult = await _stopHookRunner.RunAsync(
                request,
                state,
                session,
                settings,
                emitEvent,
                cancellationToken);
            if (stopHookResult.PreventContinuation)
            {
                return new QueryTerminalIterationResult(
                    new QueryLoopTerminal(QueryTerminalReason.StopHookPrevented),
                    state with
                    {
                        Messages = session.Messages.ToArray(),
                        ToolUseContext = toolUseContext,
                        Transition = null,
                        StopHookActive = true
                    });
            }

            if (stopHookResult.BlockingMessages.Count > 0)
            {
                return new QueryContinueIterationResult(
                    new QueryLoopTransition(QueryContinueReason.StopHookBlocking),
                    request with
                    {
                        RequestedTools = [],
                        ExecutionMode = QueryTurnExecutionMode.ModelBacked
                    },
                    state with
                    {
                        Messages = session.Messages.ToArray(),
                        TurnCount = state.TurnCount,
                        ToolUseContext = toolUseContext,
                        Transition = new QueryLoopTransition(QueryContinueReason.StopHookBlocking),
                        StopHookActive = true
                    });
            }
        }

        var finalContent = string.Join(
            Environment.NewLine + Environment.NewLine,
            toolResults.Select(
                toolResult =>
                    toolResult.Success
                        ? $"Tool {toolResult.ToolCall.ToolName} completed successfully.{Environment.NewLine}{toolResult.Output}"
                        : $"Tool {toolResult.ToolCall.ToolName} failed.{Environment.NewLine}{toolResult.Output}"));

        var assistantMessage = ChatMessageFactory.CreateText(MessageRole.Assistant, finalContent);
        await emitEvent(new QueryMessageRuntimeEvent(assistantMessage), cancellationToken);

        foreach (var chunk in Chunk(finalContent, 32))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await emitEvent(new QueryStreamDeltaRuntimeEvent(chunk), cancellationToken);
        }

        return new QueryTerminalIterationResult(
            new QueryLoopTerminal(QueryTerminalReason.Completed),
            state with
            {
                Messages = session.Messages.ToArray(),
                ToolUseContext = toolUseContext,
                Transition = null
            });
    }

    private static IEnumerable<string> Chunk(string content, int chunkSize)
    {
        for (var index = 0; index < content.Length; index += chunkSize)
        {
            var length = Math.Min(chunkSize, content.Length - index);
            yield return content.Substring(index, length);
        }
    }
}
