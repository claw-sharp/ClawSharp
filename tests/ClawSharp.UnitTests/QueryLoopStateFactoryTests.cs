// TS parity status: focused C# unit coverage for the initial query-loop state contract and defaults; the recursive loop behavior is still intentionally unported.
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public class QueryLoopStateFactoryTests
{
    [Fact]
    public void CreateInitial_Uses_Ts_Defaults()
    {
        var message = ChatMessageFactory.CreateText(MessageRole.User, "hello");

        var state = QueryLoopStateFactory.CreateInitial([message]);

        Assert.Single(state.Messages);
        Assert.Same(message, state.Messages[0]);
        Assert.Equal(1, state.TurnCount);
        Assert.Null(state.AutoCompactTracking);
        Assert.Equal(0, state.MaxOutputTokensRecoveryCount);
        Assert.False(state.HasAttemptedReactiveCompact);
        Assert.Null(state.MaxOutputTokensOverride);
        Assert.Null(state.PendingToolUseSummary);
        Assert.Null(state.StopHookActive);
        Assert.Null(state.Transition);
    }

    [Fact]
    public async Task CreateInitial_Preserves_Optional_Override_And_Pending_Summary()
    {
        var message = ChatMessageFactory.CreateText(MessageRole.User, "hello");
        var summary = QueryToolUseSummaryMessageFactory.Create(
            "summary",
            ["tool-1"]);
        var pendingToolUseSummary = Task.FromResult<QueryToolUseSummaryMessage?>(summary);

        var state = QueryLoopStateFactory.CreateInitial(
            [message],
            maxOutputTokensOverride: 64000,
            pendingToolUseSummary: pendingToolUseSummary);

        Assert.Equal(64000, state.MaxOutputTokensOverride);
        Assert.Same(pendingToolUseSummary, state.PendingToolUseSummary);
        Assert.Same(summary, await state.PendingToolUseSummary!);
    }
}
