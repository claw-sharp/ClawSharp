// TS parity status: focused C# coverage for the max_turns_reached attachment helper and detection contract; live recursive max-turn enforcement remains intentionally unported.
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public sealed class QueryMaxTurnsAttachmentContractTests
{
    [Fact]
    public void CreateMaxTurnsReachedAttachmentMessage_Stores_Ts_Shaped_Metadata()
    {
        var message = ChatMessageFactory.CreateMaxTurnsReachedAttachmentMessage(
            maxTurns: 3,
            turnCount: 4);

        Assert.Equal(MessageRole.System, message.Role);
        var block = Assert.Single(message.ContentBlocks);
        Assert.Equal(MessageContentKind.Attachment, block.Kind);
        Assert.Equal(string.Empty, block.Value);
        Assert.NotNull(block.Metadata);
        Assert.Equal("max_turns_reached", block.Metadata!["attachmentType"]);
        Assert.Equal("3", block.Metadata["maxTurns"]);
        Assert.Equal("4", block.Metadata["turnCount"]);
    }

    [Fact]
    public void TryGetMaxTurnsReachedNotification_Returns_Notification_For_MaxTurns_Attachment()
    {
        var message = ChatMessageFactory.CreateMaxTurnsReachedAttachmentMessage(
            maxTurns: 5,
            turnCount: 6);

        var parsed = QueryAttachmentHelpers.TryGetMaxTurnsReachedNotification(message, out var notification);

        Assert.True(parsed);
        Assert.NotNull(notification);
        Assert.Equal(5, notification!.MaxTurns);
        Assert.Equal(6, notification.TurnCount);
    }

    [Fact]
    public void TryGetMaxTurnsReachedNotification_Rejects_Non_MaxTurns_Attachments()
    {
        var message = ChatMessageFactory.CreateText(MessageRole.System, "not an attachment");

        var parsed = QueryAttachmentHelpers.TryGetMaxTurnsReachedNotification(message, out var notification);

        Assert.False(parsed);
        Assert.Null(notification);
    }
}
