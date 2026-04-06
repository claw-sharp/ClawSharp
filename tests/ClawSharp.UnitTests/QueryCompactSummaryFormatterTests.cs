// TS parity status: focused C# coverage for the compact-summary formatter and summary-message text contract used by the reactive-compact runtime; the live compaction model call and boundary assembly remain unported.
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public sealed class QueryCompactSummaryFormatterTests
{
    [Fact]
    public void Format_Strips_Analysis_Block_And_Rewrites_Summary_Header()
    {
        const string summary = """
            <analysis>
            Drafting details that should not be kept.
            </analysis>

            <summary>
            1. Primary Request and Intent:
               Continue Phase 4.
            </summary>
            """;

        var formatted = QueryCompactSummaryFormatter.Format(summary);

        Assert.DoesNotContain("Drafting details", formatted, StringComparison.Ordinal);
        Assert.StartsWith("Summary:", formatted, StringComparison.Ordinal);
        Assert.Contains("Continue Phase 4.", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_Appends_Transcript_Preserved_Message_And_Transcript_Metadata()
    {
        var message = QueryCompactSummaryMessageFactory.Create(
            "<summary>Key context.</summary>",
            transcriptPath: "D:\\temp\\session.jsonl",
            recentMessagesPreserved: true);

        Assert.Equal(MessageRole.User, message.Role);
        Assert.Contains($"Summary:{Environment.NewLine}Key context.", message.Content, StringComparison.Ordinal);
        Assert.Contains("read the full transcript at: D:\\temp\\session.jsonl", message.Content, StringComparison.Ordinal);
        Assert.Contains("Recent messages are preserved verbatim.", message.Content, StringComparison.Ordinal);
        var block = Assert.Single(message.ContentBlocks);
        Assert.Equal("True", block.Metadata?["isCompactSummary"]);
        Assert.Equal("True", block.Metadata?["isVisibleInTranscriptOnly"]);
        Assert.Equal("False", block.Metadata?["isMeta"]);
    }

    [Fact]
    public void Create_Appends_Follow_Up_Suppression_Instructions_When_Requested()
    {
        var message = QueryCompactSummaryMessageFactory.Create(
            "<summary>Key context.</summary>",
            suppressFollowUpQuestions: true);

        Assert.Contains(
            "Continue the conversation from where it left off without asking the user any further questions.",
            message.Content,
            StringComparison.Ordinal);
        Assert.Contains("do not acknowledge the summary", message.Content, StringComparison.Ordinal);
    }
}
