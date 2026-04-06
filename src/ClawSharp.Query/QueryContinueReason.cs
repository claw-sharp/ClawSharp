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
