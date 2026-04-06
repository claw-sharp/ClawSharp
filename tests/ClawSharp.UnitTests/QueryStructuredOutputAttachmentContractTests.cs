// TS origin: ./utils/attachments.ts, ./QueryEngine.ts
// TS parity status: focused C# coverage for the structured_output attachment helper and detection contract; live structured-output result routing remains intentionally unported.
using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public sealed class QueryStructuredOutputAttachmentContractTests
{
    [Fact]
    public void CreateStructuredOutputAttachmentMessage_Stores_Ts_Shaped_Metadata()
    {
        var data = JsonNode.Parse("""{"status":"ok","payload":{"id":7}}""")!;
        var message = ChatMessageFactory.CreateStructuredOutputAttachmentMessage(data);

        Assert.Equal(MessageRole.System, message.Role);
        var block = Assert.Single(message.ContentBlocks);
        Assert.Equal(MessageContentKind.Attachment, block.Kind);
        Assert.Equal(string.Empty, block.Value);
        Assert.NotNull(block.Metadata);
        Assert.Equal("structured_output", block.Metadata!["attachmentType"]);
        Assert.Equal("""{"status":"ok","payload":{"id":7}}""", block.Metadata["data"]);
    }

    [Fact]
    public void TryGetStructuredOutputAttachment_Returns_Attachment_For_Ts_Shaped_Message()
    {
        var message = ChatMessageFactory.CreateStructuredOutputAttachmentMessage(
            JsonNode.Parse("""{"done":true,"items":[1,2,3]}""")!);

        var parsed = QueryAttachmentHelpers.TryGetStructuredOutputAttachment(message, out var attachment);

        Assert.True(parsed);
        Assert.NotNull(attachment);
        Assert.True(attachment!.Data["done"]!.GetValue<bool>());
        Assert.Equal(3, attachment.Data["items"]!.AsArray().Count);
    }

    [Fact]
    public void TryGetStructuredOutputAttachment_Rejects_Invalid_Data_Metadata()
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
                        ["attachmentType"] = "structured_output",
                        ["data"] = "{not-json"
                    })
            ],
            DateTimeOffset.UtcNow);

        var parsed = QueryAttachmentHelpers.TryGetStructuredOutputAttachment(message, out var attachment);

        Assert.False(parsed);
        Assert.Null(attachment);
    }
}
