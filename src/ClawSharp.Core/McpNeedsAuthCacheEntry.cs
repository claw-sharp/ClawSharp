// TS origin: ./services/mcp/client.ts
using System.Text.Json.Serialization;

namespace ClawSharp.Core;

public sealed record McpNeedsAuthCacheEntry(
    [property: JsonPropertyName("timestamp")]
    long Timestamp);
