// TS origin: ./services/compact/compact.ts, ./utils/tokens.ts, ./services/tokenEstimation.ts
// TS parity status: focused C# coverage for the approved approximate compaction token-estimation seam used to carry preCompactTokenCount through the reactive-compact runtime; exact TypeScript token counting remains unported.
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public sealed class QueryCompactionTokenEstimatorTests
{
    [Fact]
    public void Estimate_Returns_Positive_Count_For_Text_And_Tool_Metadata()
    {
        var estimator = new ApproximateQueryCompactionTokenEstimator();
        var messages = new ChatMessage[]
        {
            ChatMessageFactory.CreateText(MessageRole.User, "hello world"),
            ChatMessageFactory.CreateToolUse(
                [("tooluse-1", "Read", "{\"path\":\"note.txt\"}")]),
            ChatMessageFactory.CreateToolResult(
                "tooluse-1",
                "Read",
                "file contents")
        };

        var estimate = estimator.Estimate(messages);

        Assert.True(estimate > 0);
        Assert.True(estimate >= 10);
    }
}
