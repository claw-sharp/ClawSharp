// TS origin: ./tools/ReadMcpResourceTool/ReadMcpResourceTool.ts, ./services/mcp/client.ts
namespace ClawSharp.Core;

public sealed record McpResourceContent(
    string Uri,
    string? MimeType = null,
    string? Text = null,
    byte[]? Blob = null);
