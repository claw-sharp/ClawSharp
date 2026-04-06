// TS origin: ./services/mcp/client.ts, ./services/mcp/types.ts
using System.Text.Json.Nodes;

namespace ClawSharp.Core;

public sealed record McpToolDefinition(
    string Name,
    string? Description = null,
    JsonObject? InputSchema = null,
    McpToolAnnotations? Annotations = null,
    JsonObject? Meta = null);
