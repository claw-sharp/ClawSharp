// TS origin: ./services/api/claude.ts
// TS parity status: honest placeholder for the main query-model HTTP streaming client until the TypeScript transport, auth, and event parsing paths are ported.
using System.Text.Json.Nodes;

namespace ClawSharp.Query;

public sealed class NotImplementedQueryModelHttpStreamingClient : IQueryModelHttpStreamingClient
{
    public async IAsyncEnumerable<JsonNode> StreamAsync(
        QueryModelHttpClientConfig config,
        QueryModelHttpStreamingRequest request,
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
