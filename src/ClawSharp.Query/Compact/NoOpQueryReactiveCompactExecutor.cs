// TS parity status: explicit no-op default for the reactive-compact executor seam until the concrete compaction runtime is ported.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed class NoOpQueryReactiveCompactExecutor : IQueryReactiveCompactExecutor
{
    public Task<QueryCompactionResult?> TryReactiveCompactAsync(
        QueryTurnRequest request,
        QueryLoopState priorState,
        QueryTerminalIterationResult terminalResult,
        ConversationSession session,
        ClawSharpSettings settings,
        CancellationToken cancellationToken = default,
        string trigger = "manual")
    {
        return Task.FromResult<QueryCompactionResult?>(null);
    }
}
