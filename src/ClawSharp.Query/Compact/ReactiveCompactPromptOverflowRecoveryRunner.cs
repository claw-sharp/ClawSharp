// TS parity status: ports the current TypeScript reactive-compact retry control flow under the C# prompt-overflow recovery seam using the now-ported post-compact message ordering contract; the concrete compaction executor remains delegated to an injected seam.
// TS baseline note: the real ./services/compact/reactiveCompact.ts implementation is not available in this checkout, so this runner currently treats ./services/compact/compact.ts plus ./services/compact/prompt.ts as the approved fallback baseline for the shared retry and post-compact message flow.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed class ReactiveCompactPromptOverflowRecoveryRunner : IQueryPromptOverflowRecoveryRunner
{
    public const string ReactiveCompactStatusMessage = "Compacting conversation after hitting the model's context window...";
    private readonly IQueryReactiveCompactExecutor _executor;

    public ReactiveCompactPromptOverflowRecoveryRunner(IQueryReactiveCompactExecutor? executor = null)
    {
        _executor = executor ?? new QueryReactiveCompactExecutor();
    }

    public async Task<QueryContinueIterationResult?> TryRecoverAsync(
        QueryTurnRequest request,
        QueryLoopState priorState,
        QueryTerminalIterationResult terminalResult,
        ConversationSession session,
        ClawSharpSettings settings,
        Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
        CancellationToken cancellationToken = default)
    {
        if (terminalResult.Terminal.Reason is not (QueryTerminalReason.PromptTooLong or QueryTerminalReason.ImageError) ||
            priorState.HasAttemptedReactiveCompact)
        {
            return null;
        }

        await emitEvent(
            new QueryMessageRuntimeEvent(
                ChatMessageFactory.CreateSystemMessage(ReactiveCompactStatusMessage, "info")),
            cancellationToken);

        var compacted = await _executor.TryReactiveCompactAsync(
            request,
            priorState,
            terminalResult,
            session,
            settings,
            cancellationToken);
        if (compacted is null)
        {
            return null;
        }

        var postCompactMessages = QueryPostCompactMessageBuilder.BuildPostCompactMessages(compacted);
        foreach (var message in postCompactMessages)
        {
            await emitEvent(new QueryMessageRuntimeEvent(message), cancellationToken);
        }

        var transition = new QueryLoopTransition(QueryContinueReason.ReactiveCompactRetry);
        return new QueryContinueIterationResult(
            transition,
            request,
            priorState with
            {
                Messages = postCompactMessages,
                AutoCompactTracking = null,
                HasAttemptedReactiveCompact = true,
                MaxOutputTokensOverride = null,
                PendingToolUseSummary = null,
                StopHookActive = null,
                Transition = transition
            });
    }
}
