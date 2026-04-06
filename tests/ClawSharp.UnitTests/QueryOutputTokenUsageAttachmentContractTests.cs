// TS origin: ./utils/attachments.ts, ./utils/messages.ts
// TS parity status: focused C# coverage for the output_token_usage attachment helper and detection contract; live token-budget attachment emission remains intentionally unported.
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public sealed class QueryOutputTokenUsageAttachmentContractTests
{
    [Fact]
    public void CreateOutputTokenUsageAttachmentMessage_Stores_Ts_Shaped_Metadata()
    {
        var message = ChatMessageFactory.CreateOutputTokenUsageAttachmentMessage(
            turn: 1234,
            session: 5678,
            budget: 9000);

        Assert.Equal(MessageRole.System, message.Role);
        var block = Assert.Single(message.ContentBlocks);
        Assert.Equal(MessageContentKind.Attachment, block.Kind);
        Assert.Equal(string.Empty, block.Value);
        Assert.NotNull(block.Metadata);
        Assert.Equal("output_token_usage", block.Metadata!["attachmentType"]);
        Assert.Equal("1234", block.Metadata["turn"]);
        Assert.Equal("5678", block.Metadata["session"]);
        Assert.Equal("9000", block.Metadata["budget"]);
    }

    [Fact]
    public void CreateOutputTokenUsageAttachmentMessage_Omits_Null_Budget_Metadata()
    {
        var message = ChatMessageFactory.CreateOutputTokenUsageAttachmentMessage(
            turn: 12,
            session: 34,
            budget: null);

        var block = Assert.Single(message.ContentBlocks);
        Assert.NotNull(block.Metadata);
        Assert.False(block.Metadata!.ContainsKey("budget"));
    }

    [Fact]
    public void TryGetOutputTokenUsageAttachment_Returns_Attachment_For_Ts_Shaped_Message()
    {
        var message = ChatMessageFactory.CreateOutputTokenUsageAttachmentMessage(
            turn: 77,
            session: 88,
            budget: 99);

        var parsed = QueryAttachmentHelpers.TryGetOutputTokenUsageAttachment(message, out var attachment);

        Assert.True(parsed);
        Assert.NotNull(attachment);
        Assert.Equal(77, attachment!.Turn);
        Assert.Equal(88, attachment.Session);
        Assert.Equal(99, attachment.Budget);
    }

    [Fact]
    public void TryGetOutputTokenUsageAttachment_Rejects_Invalid_Budget_Metadata()
    {
        var message = new ChatMessage(
            Guid.NewGuid().ToString("N"),
            MessageRole.System,
            [
                new MessageContentBlock(
                    MessageContentKind.Attachment,
                    string.Empty,
                    Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["attachmentType"] = "output_token_usage",
                        ["turn"] = "1",
                        ["session"] = "2",
                        ["budget"] = "not-a-number"
                    })
            ],
            DateTimeOffset.UtcNow);

        var parsed = QueryAttachmentHelpers.TryGetOutputTokenUsageAttachment(message, out var attachment);

        Assert.False(parsed);
        Assert.Null(attachment);
    }
}
