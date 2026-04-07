// TS parity status: ports the streamed model-call dependency boundary that TypeScript query() consumes via deps.callModel; live API transport, retries, and fallback execution remain blocked behind this contract in C#.
using ClawSharp.Core;

namespace ClawSharp.Query;

public interface IQueryModelCallExecutor
{
    IAsyncEnumerable<QueryModelCallUpdate> StreamAsync(
        QueryModelHttpStreamingRequest streamingRequest,
        QueryTurnRequest request,
        QueryLoopState state,
        ConversationSession session,
        ClawSharpSettings settings,
        CancellationToken cancellationToken = default);
}
