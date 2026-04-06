// TS parity status: ports the query-side account-state dependency boundary needed by the remaining TypeScript subscriber-gated retry logic.
using ClawSharp.Core;

namespace ClawSharp.Query;

public interface IQueryAuthAccountStateProvider
{
    QueryAuthAccountState GetState(
        QueryTurnRequest request,
        QueryLoopState state,
        ConversationSession session,
        ClawSharpSettings settings);
}
