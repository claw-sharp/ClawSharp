// TS parity status: focused C# unit coverage for the current usage-update, usage-accumulation, and non-streaming budget-adjustment helpers.
using System.Text.Json.Nodes;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public class QueryUsageAccumulatorTests
{
    [Fact]
    public void UpdateUsage_Preserves_NonZero_Input_Counters_And_Overwrites_Output()
    {
        var updated = QueryUsageAccumulator.UpdateUsage(
            QueryUsageSnapshot.Empty with
            {
                InputTokens = 100,
                CacheCreationInputTokens = 40,
                CacheReadInputTokens = 20,
                OutputTokens = 5
            },
            new QueryUsageDelta(
                InputTokens: 0,
                CacheCreationInputTokens: 0,
                CacheReadInputTokens: 0,
                OutputTokens: 22,
                ServerToolUse: new QueryUsageDeltaServerToolUse(WebSearchRequests: 3),
                CacheCreation: new QueryUsageDeltaCacheCreation(Ephemeral1hInputTokens: 9),
                CacheDeletedInputTokens: 0));

        Assert.Equal(100, updated.InputTokens);
        Assert.Equal(40, updated.CacheCreationInputTokens);
        Assert.Equal(20, updated.CacheReadInputTokens);
        Assert.Equal(22, updated.OutputTokens);
        Assert.Equal(3, updated.ServerToolUse.WebSearchRequests);
        Assert.Equal(0, updated.ServerToolUse.WebFetchRequests);
        Assert.Equal(9, updated.CacheCreation.Ephemeral1hInputTokens);
        Assert.Equal(0, updated.CacheCreation.Ephemeral5mInputTokens);
        Assert.Equal(0, updated.CacheDeletedInputTokens);
    }

    [Fact]
    public void AccumulateUsage_Adds_Counters_And_Uses_Most_Recent_Metadata()
    {
        var total = new QueryUsageSnapshot(
            10,
            4,
            6,
            8,
            new QueryUsageServerToolUse(1, 2),
            "standard",
            new QueryUsageCacheCreation(3, 5),
            "us",
            [1],
            "standard",
            7);
        var message = new QueryUsageSnapshot(
            11,
            12,
            13,
            14,
            new QueryUsageServerToolUse(3, 4),
            "priority",
            new QueryUsageCacheCreation(15, 16),
            "eu",
            [2, 3],
            "fast",
            9);

        var accumulated = QueryUsageAccumulator.AccumulateUsage(total, message);

        Assert.Equal(21, accumulated.InputTokens);
        Assert.Equal(16, accumulated.CacheCreationInputTokens);
        Assert.Equal(19, accumulated.CacheReadInputTokens);
        Assert.Equal(22, accumulated.OutputTokens);
        Assert.Equal(4, accumulated.ServerToolUse.WebSearchRequests);
        Assert.Equal(6, accumulated.ServerToolUse.WebFetchRequests);
        Assert.Equal("priority", accumulated.ServiceTier);
        Assert.Equal(18, accumulated.CacheCreation.Ephemeral1hInputTokens);
        Assert.Equal(21, accumulated.CacheCreation.Ephemeral5mInputTokens);
        Assert.Equal("eu", accumulated.InferenceGeo);
        Assert.Equal("fast", accumulated.Speed);
        Assert.Equal(9 + 7, accumulated.CacheDeletedInputTokens);
        Assert.Equal("[2,3]", accumulated.Iterations.ToJsonString());
    }

    [Fact]
    public void AdjustForNonStreaming_Caps_MaxTokens_And_Thinking_Budget()
    {
        var request = new QueryModelRequest(
            "session-1",
            "foundation-placeholder",
            [],
            [],
            [],
            new QueryRequestOutputConfig(),
            [],
            MaxTokens: 1000,
            Thinking: new QueryThinkingConfig("enabled", 1200));

        var adjusted = QueryOutputBudgetAdjuster.AdjustForNonStreaming(request, 600);

        Assert.Equal(600, adjusted.MaxTokens);
        Assert.Equal("enabled", adjusted.Thinking?.Type);
        Assert.Equal(599, adjusted.Thinking?.BudgetTokens);
    }

    [Fact]
    public void AdjustForNonStreaming_Leaves_Request_Unchanged_Without_MaxTokens()
    {
        var request = new QueryModelRequest(
            "session-1",
            "foundation-placeholder",
            [],
            [],
            [],
            new QueryRequestOutputConfig(),
            [],
            Thinking: new QueryThinkingConfig("disabled"));

        var adjusted = QueryOutputBudgetAdjuster.AdjustForNonStreaming(request, 600);

        Assert.Null(adjusted.MaxTokens);
        Assert.Equal("disabled", adjusted.Thinking?.Type);
    }
}
