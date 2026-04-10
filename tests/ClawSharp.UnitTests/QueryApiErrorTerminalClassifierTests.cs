using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public sealed class QueryApiErrorTerminalClassifierTests
{
    [Fact]
    public void Classify_Treats_ContextWindowOverflow_Message_As_PromptTooLong()
    {
        var message = ChatMessageFactory.CreateAssistantApiErrorMessage(
            "Your input exceeds the context window of this model. Please adjust your input and try again.",
            apiError: "invalid_request",
            error: "invalid_request");

        var terminal = QueryApiErrorTerminalClassifier.Classify(message);

        Assert.NotNull(terminal);
        Assert.Equal(QueryTerminalReason.PromptTooLong, terminal!.Reason);
    }

    [Fact]
    public void Classify_Treats_ContextWindowOverflow_Details_As_PromptTooLong()
    {
        var message = ChatMessageFactory.CreateAssistantApiErrorMessage(
            "Query model request failed with API response (400).",
            apiError: "invalid_request",
            error: "invalid_request",
            errorDetails: "{\"error\":{\"message\":\"Your input exceeds the context window of this model. Please adjust your input and try again.\"}}");

        var terminal = QueryApiErrorTerminalClassifier.Classify(message);

        Assert.NotNull(terminal);
        Assert.Equal(QueryTerminalReason.PromptTooLong, terminal!.Reason);
    }

    [Fact]
    public void Classify_Maps_Repeated_ContextWindowOverflow_After_CollapseRetry_To_BlockingLimit()
    {
        var message = ChatMessageFactory.CreateAssistantApiErrorMessage(
            "Your input exceeds the context window of this model. Please adjust your input and try again.",
            apiError: "invalid_request",
            error: "invalid_request");
        var priorState = QueryLoopStateFactory.CreateInitial(
            [ChatMessageFactory.CreateText(MessageRole.User, "hello")]) with
        {
            Transition = new QueryLoopTransition(QueryContinueReason.CollapseDrainRetry, Committed: 1)
        };

        var terminal = QueryApiErrorTerminalClassifier.Classify(message, priorState);

        Assert.NotNull(terminal);
        Assert.Equal(QueryTerminalReason.BlockingLimit, terminal!.Reason);
    }
}
