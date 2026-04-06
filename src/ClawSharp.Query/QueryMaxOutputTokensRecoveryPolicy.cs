// TS parity status: ports the current TypeScript max_output_tokens recovery decision branch, including one-shot escalation and bounded recovery-message retries.
namespace ClawSharp.Query;

public static class QueryMaxOutputTokensRecoveryPolicy
{
    public const int MaxOutputTokensRecoveryLimit = 3;
    public const int EscalatedMaxTokens = 64_000;
    public const string RecoveryMessageContent =
        "Output token limit hit. Resume directly — no apology, no recap of what you were doing. " +
        "Pick up mid-thought if that is where the cut happened. Break remaining work into smaller pieces.";

    public static QueryMaxOutputTokensRecoveryDecision? Evaluate(
        bool capEnabled,
        bool isWithheldMaxOutputTokens,
        int recoveryCount,
        int? maxOutputTokensOverride,
        bool hasEnvironmentMaxOutputTokensOverride)
    {
        if (!isWithheldMaxOutputTokens)
        {
            return null;
        }

        if (capEnabled &&
            maxOutputTokensOverride is null &&
            !hasEnvironmentMaxOutputTokensOverride)
        {
            return new QueryMaxOutputTokensRecoveryDecision(
                new QueryLoopTransition(QueryContinueReason.MaxOutputTokensEscalate),
                NextMaxOutputTokensOverride: EscalatedMaxTokens,
                NextRecoveryCount: recoveryCount);
        }

        if (recoveryCount < MaxOutputTokensRecoveryLimit)
        {
            return new QueryMaxOutputTokensRecoveryDecision(
                new QueryLoopTransition(
                    QueryContinueReason.MaxOutputTokensRecovery,
                    Attempt: recoveryCount + 1),
                NextMaxOutputTokensOverride: null,
                NextRecoveryCount: recoveryCount + 1,
                RecoveryMessageContent: RecoveryMessageContent);
        }

        return null;
    }
}
