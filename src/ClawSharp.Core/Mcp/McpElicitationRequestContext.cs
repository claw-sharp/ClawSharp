namespace ClawSharp.Core;

public sealed record McpElicitationRequestContext(
    string RequestId,
    McpElicitRequestParams Params,
    CancellationToken CancellationToken);
