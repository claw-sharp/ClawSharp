// TS origin: ./services/api/emptyUsage.ts, ./services/api/logging.ts, ./services/api/claude.ts
// TS parity status: ports the non-null usage snapshot carried through the current TypeScript query runtime; element-level iteration payloads remain opaque until the model transport is ported.
using System.Text.Json.Nodes;

namespace ClawSharp.Query;

public sealed record QueryUsageSnapshot(
    int InputTokens,
    int CacheCreationInputTokens,
    int CacheReadInputTokens,
    int OutputTokens,
    QueryUsageServerToolUse ServerToolUse,
    string ServiceTier,
    QueryUsageCacheCreation CacheCreation,
    string InferenceGeo,
    JsonArray Iterations,
    string Speed,
    int CacheDeletedInputTokens = 0)
{
    public static QueryUsageSnapshot Empty { get; } =
        new(
            0,
            0,
            0,
            0,
            new QueryUsageServerToolUse(0, 0),
            "standard",
            new QueryUsageCacheCreation(0, 0),
            string.Empty,
            [],
            "standard");
}
