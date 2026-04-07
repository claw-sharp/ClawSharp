// TS parity status: ports the current TypeScript usage update and accumulate helpers used by the query streaming and non-streaming fallback paths.
using System.Text.Json.Nodes;

namespace ClawSharp.Query;

public static class QueryUsageAccumulator
{
    public static QueryUsageSnapshot UpdateUsage(
        QueryUsageSnapshot usage,
        QueryUsageDelta? partUsage)
    {
        if (partUsage is null)
        {
            return usage with
            {
                Iterations = DeepCloneArray(usage.Iterations)
            };
        }

        return new QueryUsageSnapshot(
            partUsage.InputTokens is > 0 ? partUsage.InputTokens.Value : usage.InputTokens,
            partUsage.CacheCreationInputTokens is > 0 ? partUsage.CacheCreationInputTokens.Value : usage.CacheCreationInputTokens,
            partUsage.CacheReadInputTokens is > 0 ? partUsage.CacheReadInputTokens.Value : usage.CacheReadInputTokens,
            partUsage.OutputTokens ?? usage.OutputTokens,
            new QueryUsageServerToolUse(
                partUsage.ServerToolUse?.WebSearchRequests ?? usage.ServerToolUse.WebSearchRequests,
                partUsage.ServerToolUse?.WebFetchRequests ?? usage.ServerToolUse.WebFetchRequests),
            usage.ServiceTier,
            new QueryUsageCacheCreation(
                partUsage.CacheCreation?.Ephemeral1hInputTokens ?? usage.CacheCreation.Ephemeral1hInputTokens,
                partUsage.CacheCreation?.Ephemeral5mInputTokens ?? usage.CacheCreation.Ephemeral5mInputTokens),
            usage.InferenceGeo,
            DeepCloneArray(partUsage.Iterations ?? usage.Iterations),
            partUsage.Speed ?? usage.Speed,
            partUsage.CacheDeletedInputTokens is > 0 ? partUsage.CacheDeletedInputTokens.Value : usage.CacheDeletedInputTokens);
    }

    public static QueryUsageSnapshot AccumulateUsage(
        QueryUsageSnapshot totalUsage,
        QueryUsageSnapshot messageUsage)
    {
        return new QueryUsageSnapshot(
            totalUsage.InputTokens + messageUsage.InputTokens,
            totalUsage.CacheCreationInputTokens + messageUsage.CacheCreationInputTokens,
            totalUsage.CacheReadInputTokens + messageUsage.CacheReadInputTokens,
            totalUsage.OutputTokens + messageUsage.OutputTokens,
            new QueryUsageServerToolUse(
                totalUsage.ServerToolUse.WebSearchRequests + messageUsage.ServerToolUse.WebSearchRequests,
                totalUsage.ServerToolUse.WebFetchRequests + messageUsage.ServerToolUse.WebFetchRequests),
            messageUsage.ServiceTier,
            new QueryUsageCacheCreation(
                totalUsage.CacheCreation.Ephemeral1hInputTokens + messageUsage.CacheCreation.Ephemeral1hInputTokens,
                totalUsage.CacheCreation.Ephemeral5mInputTokens + messageUsage.CacheCreation.Ephemeral5mInputTokens),
            messageUsage.InferenceGeo,
            DeepCloneArray(messageUsage.Iterations),
            messageUsage.Speed,
            totalUsage.CacheDeletedInputTokens + messageUsage.CacheDeletedInputTokens);
    }

    private static JsonArray DeepCloneArray(JsonArray array)
    {
        return JsonNode.Parse(array.ToJsonString()) as JsonArray ?? [];
    }
}
