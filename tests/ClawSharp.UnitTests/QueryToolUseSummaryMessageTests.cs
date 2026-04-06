// TS parity status: focused C# coverage for the tool_use_summary contract and loop-state pending-summary slot; live query/runtime emission is still intentionally unported.
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public class QueryToolUseSummaryMessageTests
{
    [Fact]
    public void Create_Preserves_Summary_And_Preceding_Tool_Use_Ids()
    {
        var message = QueryToolUseSummaryMessageFactory.Create(
            "Read two files and updated one test.",
            ["tool-1", "tool-2"]);

        Assert.Equal("Read two files and updated one test.", message.Summary);
        Assert.Equal(["tool-1", "tool-2"], message.PrecedingToolUseIds);
        Assert.False(string.IsNullOrWhiteSpace(message.Id));
    }

    [Fact]
    public async Task QueryLoopStateFactory_Preserves_Pending_Tool_Use_Summary_Task()
    {
        var message = ChatMessageFactory.CreateText(MessageRole.User, "hello");
        var summary = QueryToolUseSummaryMessageFactory.Create(
            "Ran Bash and Read.",
            ["tool-1"]);
        var pendingToolUseSummary = Task.FromResult<QueryToolUseSummaryMessage?>(summary);

        var state = QueryLoopStateFactory.CreateInitial(
            [message],
            pendingToolUseSummary: pendingToolUseSummary);

        Assert.Same(pendingToolUseSummary, state.PendingToolUseSummary);
        Assert.Same(summary, await state.PendingToolUseSummary!);
    }
}
