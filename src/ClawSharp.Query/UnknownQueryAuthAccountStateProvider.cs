// TS origin: ./utils/auth.ts, ./services/api/withRetry.ts
// TS parity status: explicit no-op fallback for the query-side account-state dependency when the real Claude AI OAuth state source has not been wired into the runtime.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed class UnknownQueryAuthAccountStateProvider : IQueryAuthAccountStateProvider
{
    public QueryAuthAccountState GetState(
        QueryTurnRequest request,
        QueryLoopState state,
        ConversationSession session,
        ClawSharpSettings settings)
    {
        return QueryAuthAccountState.None;
    }
}
