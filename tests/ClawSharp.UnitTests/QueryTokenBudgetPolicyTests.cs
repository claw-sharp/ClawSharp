// TS origin: ./query/tokenBudget.ts, ./utils/tokenBudget.ts
// TS parity status: focused C# coverage for the token-budget tracker thresholds, continuation message, and diminishing-returns stop branch now used by the live model-backed query loop.
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public sealed class QueryTokenBudgetPolicyTests
{
    [Fact]
    public void Evaluate_Continues_Below_Completion_Threshold_And_Updates_Tracker()
    {
        var startedAt = new DateTimeOffset(2026, 4, 2, 8, 0, 0, TimeSpan.Zero);
        var tracker = QueryTokenBudgetTracker.Create(startedAt);

        var decision = QueryTokenBudgetPolicy.Evaluate(
            tracker,
            budget: 1_000,
            globalTurnTokens: 400,
            now: startedAt.AddSeconds(5));

        var continueDecision = Assert.IsType<QueryTokenBudgetContinueDecision>(decision);
        Assert.Equal(1, continueDecision.ContinuationCount);
        Assert.Equal(40, continueDecision.Pct);
        Assert.Equal(400, continueDecision.TurnTokens);
        Assert.Equal(1_000, continueDecision.Budget);
        Assert.Equal(
            "Stopped at 40% of token target (400 / 1,000). Keep working — do not summarize.",
            continueDecision.NudgeMessage);
        Assert.Equal(1, continueDecision.Tracker.ContinuationCount);
        Assert.Equal(400, continueDecision.Tracker.LastDeltaTokens);
        Assert.Equal(400, continueDecision.Tracker.LastGlobalTurnTokens);
    }

    [Fact]
    public void Evaluate_Stops_On_Diminishing_Returns_After_Multiple_Continuations()
    {
        var startedAt = new DateTimeOffset(2026, 4, 2, 8, 0, 0, TimeSpan.Zero);
        var tracker = new QueryTokenBudgetTracker(
            ContinuationCount: 3,
            LastDeltaTokens: 200,
            LastGlobalTurnTokens: 1_200,
            StartedAt: startedAt);

        var decision = QueryTokenBudgetPolicy.Evaluate(
            tracker,
            budget: 5_000,
            globalTurnTokens: 1_500,
            now: startedAt.AddSeconds(12));

        var stopDecision = Assert.IsType<QueryTokenBudgetStopDecision>(decision);
        Assert.NotNull(stopDecision.CompletionEvent);
        Assert.Equal(3, stopDecision.CompletionEvent!.ContinuationCount);
        Assert.Equal(30, stopDecision.CompletionEvent.Pct);
        Assert.Equal(1_500, stopDecision.CompletionEvent.TurnTokens);
        Assert.Equal(5_000, stopDecision.CompletionEvent.Budget);
        Assert.True(stopDecision.CompletionEvent.DiminishingReturns);
        Assert.Equal(12_000, stopDecision.CompletionEvent.DurationMs);
    }
}
