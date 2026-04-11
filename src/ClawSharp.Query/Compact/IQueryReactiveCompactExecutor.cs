// TS parity status: ports the reactive-compact execution seam beneath the C# prompt-overflow recovery path; concrete summarization, strip-retry, and cache-safe parameter handling remain delegated to later runtime ports.
using ClawSharp.Core;

namespace ClawSharp.Query;

public interface IQueryReactiveCompactExecutor
{
    Task<QueryCompactionResult?> TryReactiveCompactAsync(
        QueryTurnRequest request,
        QueryLoopState priorState,
        QueryTerminalIterationResult terminalResult,
        ConversationSession session,
        ClawSharpSettings settings,
        CancellationToken cancellationToken = default,
        string trigger = "manual");
}
