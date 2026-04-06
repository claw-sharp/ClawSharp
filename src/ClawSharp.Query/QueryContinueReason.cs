// TS origin: ./query.ts
namespace ClawSharp.Query;

public enum QueryContinueReason
{
    CollapseDrainRetry,
    ReactiveCompactRetry,
    MaxOutputTokensEscalate,
    MaxOutputTokensRecovery,
    StopHookBlocking,
    TokenBudgetContinuation,
    NextTurn
}
