// TS parity status: explicit no-op fallback for the auth-failure recovery seam until a concrete Claude AI OAuth refresh path is wired into the query runtime.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed class NoOpQueryAuthFailureRecoveryRunner : IQueryAuthFailureRecoveryRunner
{
    public Task RunAsync(
        QueryModelApiException exception,
        QueryTurnRequest request,
        QueryLoopState state,
        ConversationSession session,
        ClawSharpSettings settings,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
