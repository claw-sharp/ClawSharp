// TS parity status: ports the query-loop stop-hook execution boundary so the outer query iteration can depend on a streamed Stop-hook runner without taking a direct dependency on infrastructure hook execution details.
using ClawSharp.Core;

namespace ClawSharp.Query;

public interface IQueryStopHookRunner
{
    Task<QueryStopHookExecutionResult> RunAsync(
        QueryTurnRequest request,
        QueryLoopState state,
        ConversationSession session,
        ClawSharpSettings settings,
        Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
        CancellationToken cancellationToken = default);
}

public sealed record QueryStopHookExecutionResult(
    bool PreventContinuation,
    IReadOnlyList<ChatMessage> BlockingMessages)
{
    public static QueryStopHookExecutionResult Empty { get; } = new(false, []);
}
