// TS parity status: ports the current token-budget tracker shape used by the TypeScript query loop; the closest viable C# equivalent is carried on QueryLoopState because the current loop splits per-iteration execution behind IQueryIterationRunner instead of keeping one mutable local closure.
namespace ClawSharp.Query;

public sealed record QueryTokenBudgetTracker(
    int ContinuationCount,
    int LastDeltaTokens,
    int LastGlobalTurnTokens,
    DateTimeOffset StartedAt)
{
    public static QueryTokenBudgetTracker Create(DateTimeOffset? startedAt = null)
    {
        return new QueryTokenBudgetTracker(
            ContinuationCount: 0,
            LastDeltaTokens: 0,
            LastGlobalTurnTokens: 0,
            StartedAt: startedAt ?? DateTimeOffset.UtcNow);
    }
}
