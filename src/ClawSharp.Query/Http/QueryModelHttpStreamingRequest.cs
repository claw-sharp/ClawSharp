// TS parity status: ports the main query-model streaming request envelope carried into the transport layer from the already-built query request snapshot.
namespace ClawSharp.Query;

public sealed record QueryModelHttpStreamingRequest(
    QueryModelRequest Request,
    string QuerySource);
