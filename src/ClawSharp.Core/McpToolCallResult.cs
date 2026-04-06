using System.Text.Json.Nodes;

namespace ClawSharp.Core;

public sealed record McpToolCallResult(
    string Content,
    JsonObject? Meta = null,
    JsonNode? StructuredContent = null);
