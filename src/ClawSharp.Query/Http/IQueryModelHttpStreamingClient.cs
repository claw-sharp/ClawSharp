// TS parity status: ports the main query-model HTTP streaming client boundary beneath the model-call executor; raw streamed payload parsing remains unported.
using System.Text.Json.Nodes;

namespace ClawSharp.Query;

public interface IQueryModelHttpStreamingClient
{
    IAsyncEnumerable<JsonNode> StreamAsync(
        QueryModelHttpClientConfig config,
        QueryModelHttpStreamingRequest request,
        CancellationToken cancellationToken = default);
}
