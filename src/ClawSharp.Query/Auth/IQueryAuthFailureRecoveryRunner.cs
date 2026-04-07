// TS parity status: ports the query-side auth-failure recovery callback seam used before rebuilding the model client after 401 and revoked-token OAuth failures.
using ClawSharp.Core;

namespace ClawSharp.Query;

public interface IQueryAuthFailureRecoveryRunner
{
    Task RunAsync(
        QueryModelApiException exception,
        QueryTurnRequest request,
        QueryLoopState state,
        ConversationSession session,
        ClawSharpSettings settings,
        CancellationToken cancellationToken = default);
}
