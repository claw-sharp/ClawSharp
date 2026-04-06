// TS origin: ./services/mcp/elicitationHandler.ts, ./services/mcp/client.ts
namespace ClawSharp.Core;

public abstract record McpElicitRequestParams(
    string Mode,
    string Message);

public sealed record McpFormElicitRequestParams(
    string Message,
    IReadOnlyDictionary<string, object?>? RequestedSchema = null)
    : McpElicitRequestParams("form", Message);

public sealed record McpUrlElicitRequestParams(
    string Message,
    string Url,
    string ElicitationId)
    : McpElicitRequestParams("url", Message);
