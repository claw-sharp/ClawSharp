// TS parity status: closest viable C# port of the current TypeScript collapse-drain retry path using the existing compact-boundary message contract; the broader context-collapse runtime and committed-summary queue are still unported, so this recovery only drains to the last persisted compact boundary when one already exists in loop state.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed class CompactBoundaryPromptOverflowRecoveryRunner : IQueryPromptOverflowRecoveryRunner
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
        if (terminalResult.Terminal.Reason != QueryTerminalReason.PromptTooLong ||
            priorState.Transition?.Reason == QueryContinueReason.CollapseDrainRetry)
        {
            return Task.FromResult<QueryContinueIterationResult?>(null);
        }

        var boundaryIndex = QueryCompactBoundaryHelpers.FindLastCompactBoundaryIndex(priorState.Messages);
        if (boundaryIndex <= 0)
        {
            return Task.FromResult<QueryContinueIterationResult?>(null);
        }

        var drainedMessages = QueryCompactBoundaryHelpers.GetMessagesAfterCompactBoundary(priorState.Messages);
        var transition = new QueryLoopTransition(
            QueryContinueReason.CollapseDrainRetry,
            Committed: boundaryIndex);

        return Task.FromResult<QueryContinueIterationResult?>(
            new QueryContinueIterationResult(
                transition,
                request,
                priorState with
                {
                    Messages = drainedMessages,
                    MaxOutputTokensOverride = null,
                    PendingToolUseSummary = null,
                    StopHookActive = null,
                    Transition = transition
                }));
    }
}
