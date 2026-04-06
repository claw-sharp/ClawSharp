// TS origin: ./services/mcp/elicitationHandler.ts
namespace ClawSharp.Core;

public sealed record McpElicitationRequestContext(
    string RequestId,
    McpElicitRequestParams Params,
    CancellationToken CancellationToken);
