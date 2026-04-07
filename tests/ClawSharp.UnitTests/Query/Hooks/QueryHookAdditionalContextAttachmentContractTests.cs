// TS parity status: focused C# coverage for the hook_additional_context attachment helper and detection contract; live hook execution remains intentionally unported.
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public sealed class QueryHookAdditionalContextAttachmentContractTests
{
    [Fact]
    public void CreateHookAdditionalContextAttachmentMessage_Stores_Ts_Shaped_Metadata()
    {
        var message = ChatMessageFactory.CreateHookAdditionalContextAttachmentMessage(
            ["line 1", "line 2"],
            hookName: "Stop",
            toolUseId: "tool-1",
            hookEvent: HookEvent.Stop);

        Assert.Equal(MessageRole.System, message.Role);
        var block = Assert.Single(message.ContentBlocks);
        Assert.Equal(MessageContentKind.Attachment, block.Kind);
        Assert.Equal(string.Empty, block.Value);
        Assert.NotNull(block.Metadata);
        Assert.Equal("hook_additional_context", block.Metadata!["attachmentType"]);
        Assert.Equal("""["line 1","line 2"]""", block.Metadata["content"]);
        Assert.Equal("Stop", block.Metadata["hookName"]);
        Assert.Equal("tool-1", block.Metadata["toolUseID"]);
        Assert.Equal("Stop", block.Metadata["hookEvent"]);
    }

    [Fact]
    public void TryGetHookAdditionalContextAttachment_Returns_Attachment_For_Ts_Shaped_Message()
    {
        var message = ChatMessageFactory.CreateHookAdditionalContextAttachmentMessage(
            ["first", "second"],
            hookName: "TaskCompleted",
            toolUseId: "tool-2",
            hookEvent: HookEvent.TaskCompleted);

        var parsed = QueryAttachmentHelpers.TryGetHookAdditionalContextAttachment(message, out var attachment);

        Assert.True(parsed);
        Assert.NotNull(attachment);
        Assert.Equal(["first", "second"], attachment!.Content);
        Assert.Equal("TaskCompleted", attachment.HookName);
        Assert.Equal("tool-2", attachment.ToolUseId);
        Assert.Equal(HookEvent.TaskCompleted, attachment.HookEvent);
    }

    [Fact]
    public void TryGetHookAdditionalContextAttachment_Rejects_Invalid_Content_Metadata()
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
                        ["attachmentType"] = "hook_additional_context",
                        ["content"] = "{bad-json",
                        ["hookName"] = "Stop",
                        ["toolUseID"] = "tool-3",
                        ["hookEvent"] = "Stop"
                    })
            ],
            DateTimeOffset.UtcNow);

        var parsed = QueryAttachmentHelpers.TryGetHookAdditionalContextAttachment(message, out var attachment);

        Assert.False(parsed);
        Assert.Null(attachment);
    }
}
