// TS parity status: focused C# unit coverage for the max_output_tokens recovery decision helper; the actual query-loop wiring still depends on the unported model-backed runtime.
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public class QueryMaxOutputTokensRecoveryPolicyTests
{
    [Fact]
    public void Evaluate_Returns_Null_When_Message_Is_Not_Withheld_Max_Output_Tokens()
    {
        var decision = QueryMaxOutputTokensRecoveryPolicy.Evaluate(
            capEnabled: true,
            isWithheldMaxOutputTokens: false,
            recoveryCount: 0,
            maxOutputTokensOverride: null,
            hasEnvironmentMaxOutputTokensOverride: false);

        Assert.Null(decision);
    }

    [Fact]
    public void Evaluate_Returns_Escalate_Decision_When_Cap_Path_Is_Eligible()
    {
        var decision = QueryMaxOutputTokensRecoveryPolicy.Evaluate(
            capEnabled: true,
            isWithheldMaxOutputTokens: true,
            recoveryCount: 0,
            maxOutputTokensOverride: null,
            hasEnvironmentMaxOutputTokensOverride: false);

        Assert.NotNull(decision);
        Assert.Equal(QueryContinueReason.MaxOutputTokensEscalate, decision!.Transition.Reason);
        Assert.Equal(QueryMaxOutputTokensRecoveryPolicy.EscalatedMaxTokens, decision.NextMaxOutputTokensOverride);
        Assert.Equal(0, decision.NextRecoveryCount);
        Assert.Null(decision.RecoveryMessageContent);
    }

    [Fact]
    public void Evaluate_Returns_Recovery_Decision_When_Escalation_Is_Not_Eligible()
    {
        var decision = QueryMaxOutputTokensRecoveryPolicy.Evaluate(
            capEnabled: false,
            isWithheldMaxOutputTokens: true,
            recoveryCount: 1,
            maxOutputTokensOverride: QueryMaxOutputTokensRecoveryPolicy.EscalatedMaxTokens,
            hasEnvironmentMaxOutputTokensOverride: false);

        Assert.NotNull(decision);
        Assert.Equal(QueryContinueReason.MaxOutputTokensRecovery, decision!.Transition.Reason);
        Assert.Equal(2, decision.Transition.Attempt);
        Assert.Equal(2, decision.NextRecoveryCount);
        Assert.Null(decision.NextMaxOutputTokensOverride);
        Assert.Equal(QueryMaxOutputTokensRecoveryPolicy.RecoveryMessageContent, decision.RecoveryMessageContent);
    }

    [Fact]
    public void Evaluate_Returns_Null_When_Recovery_Limit_Is_Exhausted()
    {
        var decision = QueryMaxOutputTokensRecoveryPolicy.Evaluate(
            capEnabled: false,
            isWithheldMaxOutputTokens: true,
            recoveryCount: QueryMaxOutputTokensRecoveryPolicy.MaxOutputTokensRecoveryLimit,
            maxOutputTokensOverride: QueryMaxOutputTokensRecoveryPolicy.EscalatedMaxTokens,
            hasEnvironmentMaxOutputTokensOverride: true);

        Assert.Null(decision);
    }
}
