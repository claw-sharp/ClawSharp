// TS origin: ./query/deps.ts, ./services/api/claude.ts
// TS parity status: honest placeholder for the streamed model-call dependency until the TypeScript model runtime is ported; preserves the existing not-implemented boundary without inventing fallback or streaming behavior.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed class NotImplementedQueryModelCallExecutor : IQueryModelCallExecutor
{
    public async IAsyncEnumerable<QueryModelCallUpdate> StreamAsync(
        QueryModelHttpStreamingRequest streamingRequest,
        QueryTurnRequest request,
        QueryLoopState state,
        ConversationSession session,
        ClawSharpSettings settings,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        if (cancellationToken.IsCancellationRequested)
        {
            yield break;
        }

        throw new QueryExecutionNotImplementedException();
    }
}
