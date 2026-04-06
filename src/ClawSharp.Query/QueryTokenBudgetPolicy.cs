// TS parity status: ports the current TypeScript token-budget thresholds, continuation message, diminishing-returns rule, and completion-event shape; the current C# runtime still lacks the TS agentId gate, so this helper only evaluates the budget and output-token inputs that ClawSharp can presently carry in the live query loop.
namespace ClawSharp.Query;

public static class QueryTokenBudgetPolicy
{
    public const double CompletionThreshold = 0.9d;
    public const int DiminishingThreshold = 500;

    public static QueryTokenBudgetDecision Evaluate(
        QueryTokenBudgetTracker tracker,
        int? budget,
        int globalTurnTokens,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(tracker);

        if (budget is null || budget <= 0)
        {
            return new QueryTokenBudgetStopDecision(tracker, null);
        }

        var pct = (int)Math.Round((double)globalTurnTokens / budget.Value * 100d, MidpointRounding.AwayFromZero);
        var deltaSinceLastCheck = globalTurnTokens - tracker.LastGlobalTurnTokens;

        var isDiminishing =
            tracker.ContinuationCount >= 3 &&
            deltaSinceLastCheck < DiminishingThreshold &&
            tracker.LastDeltaTokens < DiminishingThreshold;

        if (!isDiminishing && globalTurnTokens < budget.Value * CompletionThreshold)
        {
            var nextTracker = tracker with
            {
                ContinuationCount = tracker.ContinuationCount + 1,
                LastDeltaTokens = deltaSinceLastCheck,
                LastGlobalTurnTokens = globalTurnTokens
            };

            return new QueryTokenBudgetContinueDecision(
                nextTracker,
                GetBudgetContinuationMessage(pct, globalTurnTokens, budget.Value),
                nextTracker.ContinuationCount,
                pct,
                globalTurnTokens,
                budget.Value);
        }

        if (isDiminishing || tracker.ContinuationCount > 0)
        {
            return new QueryTokenBudgetStopDecision(
                tracker,
                new QueryTokenBudgetCompletionEvent(
                    tracker.ContinuationCount,
                    pct,
                    globalTurnTokens,
                    budget.Value,
                    isDiminishing,
                    (long)((now ?? DateTimeOffset.UtcNow) - tracker.StartedAt).TotalMilliseconds));
        }

        return new QueryTokenBudgetStopDecision(tracker, null);
    }

    public static string GetBudgetContinuationMessage(int pct, int turnTokens, int budget)
    {
        return $"Stopped at {pct}% of token target ({turnTokens.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("en-US"))} / {budget.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("en-US"))}). Keep working — do not summarize.";
    }
}

public abstract record QueryTokenBudgetDecision(QueryTokenBudgetTracker Tracker);

public sealed record QueryTokenBudgetContinueDecision(
    QueryTokenBudgetTracker Tracker,
    string NudgeMessage,
    int ContinuationCount,
    int Pct,
    int TurnTokens,
    int Budget) : QueryTokenBudgetDecision(Tracker);

public sealed record QueryTokenBudgetStopDecision(
    QueryTokenBudgetTracker Tracker,
    QueryTokenBudgetCompletionEvent? CompletionEvent) : QueryTokenBudgetDecision(Tracker);

public sealed record QueryTokenBudgetCompletionEvent(
    int ContinuationCount,
    int Pct,
    int TurnTokens,
    int Budget,
    bool DiminishingReturns,
    long DurationMs);
