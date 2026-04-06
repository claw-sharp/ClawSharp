// TS origin: ./services/mcp/elicitationHandler.ts, ./services/mcp/client.ts
namespace ClawSharp.Core;

public sealed record McpElicitResult(
    string Action,
    IReadOnlyDictionary<string, object?>? Content = null);
