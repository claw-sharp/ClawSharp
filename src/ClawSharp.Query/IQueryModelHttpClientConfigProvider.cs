// TS origin: ./services/api/claude.ts
// TS parity status: ports the main query-model client-config boundary that TypeScript resolves before dispatching a streaming request; live auth/provider selection remains external to this contract.
using ClawSharp.Core;

namespace ClawSharp.Query;

public interface IQueryModelHttpClientConfigProvider
{
    QueryModelHttpClientConfig GetConfig(
        QueryTurnRequest request,
        QueryLoopState state,
        ConversationSession session,
        ClawSharpSettings settings);
}
