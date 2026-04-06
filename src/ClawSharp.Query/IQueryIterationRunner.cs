// TS origin: ./query.ts
// TS parity status: ports the core per-iteration execution boundary used by the TypeScript query loop; full model-backed iteration behavior remains blocked on the missing transport/runtime path.
using ClawSharp.Core;

namespace ClawSharp.Query;

public interface IQueryIterationRunner
{
    Task<QueryIterationResult> RunAsync(
        QueryTurnRequest request,
        QueryLoopState state,
        ConversationSession session,
        ClawSharpSettings settings,
        Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
        CancellationToken cancellationToken = default);
}
