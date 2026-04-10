namespace ClawSharp.Query;

public enum QueryContinueReason
{
    AutoCompactRetry,
    CollapseDrainRetry,
    ReactiveCompactRetry,
    MaxOutputTokensEscalate,
    MaxOutputTokensRecovery,
    StopHookBlocking,
    TokenBudgetContinuation,
    NextTurn
}
