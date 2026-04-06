// TS origin: ./services/mcp/client.ts
namespace ClawSharp.Core;

public sealed record McpToolAnnotations(
    bool? ReadOnlyHint = null,
    bool? DestructiveHint = null,
    bool? OpenWorldHint = null,
    string? Title = null);
