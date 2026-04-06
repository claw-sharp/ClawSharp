// TS parity status: ports the partial usage payload merged into cumulative usage during the current TypeScript streaming and fallback paths.
using System.Text.Json.Nodes;

namespace ClawSharp.Query;

public sealed record QueryUsageDelta(
    int? InputTokens = null,
    int? CacheCreationInputTokens = null,
    int? CacheReadInputTokens = null,
    int? OutputTokens = null,
    QueryUsageDeltaServerToolUse? ServerToolUse = null,
    string? ServiceTier = null,
    QueryUsageDeltaCacheCreation? CacheCreation = null,
    int? CacheDeletedInputTokens = null,
    string? InferenceGeo = null,
    JsonArray? Iterations = null,
    string? Speed = null);
