// TS origin: ./services/mcp/client.ts, ./services/mcp/types.ts
namespace ClawSharp.Core;

public sealed record McpServerResource(
    string Server,
    string Uri,
    string Name,
    string? MimeType = null,
    string? Description = null);
