// TS parity status: focused C# coverage for the hook_error_during_execution attachment helper and detection contract; live hook execution remains intentionally unported.
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public sealed class QueryHookErrorDuringExecutionAttachmentContractTests
{
    [Fact]
    public void CreateHookErrorDuringExecutionAttachmentMessage_Stores_Ts_Shaped_Metadata()
    {
        var message = ChatMessageFactory.CreateHookErrorDuringExecutionAttachmentMessage(
            content: "hook crashed",
            hookName: "Stop",
            toolUseId: "tool-1",
            hookEvent: HookEvent.Stop,
            command: "echo nope",
            durationMs: 321);

        Assert.Equal(MessageRole.System, message.Role);
        var block = Assert.Single(message.ContentBlocks);
        Assert.Equal(MessageContentKind.Attachment, block.Kind);
        Assert.Equal(string.Empty, block.Value);
        Assert.NotNull(block.Metadata);
        Assert.Equal("hook_error_during_execution", block.Metadata!["attachmentType"]);
        Assert.Equal("hook crashed", block.Metadata["content"]);
        Assert.Equal("Stop", block.Metadata["hookName"]);
        Assert.Equal("tool-1", block.Metadata["toolUseID"]);
        Assert.Equal("Stop", block.Metadata["hookEvent"]);
        Assert.Equal("echo nope", block.Metadata["command"]);
        Assert.Equal("321", block.Metadata["durationMs"]);
    }

    [Fact]
    public void TryGetHookErrorDuringExecutionAttachment_Returns_Attachment_For_Ts_Shaped_Message()
    {
        var message = ChatMessageFactory.CreateHookErrorDuringExecutionAttachmentMessage(
            content: "boom",
            hookName: "TaskCompleted",
            toolUseId: "tool-2",
            hookEvent: HookEvent.TaskCompleted);

        var parsed = QueryAttachmentHelpers.TryGetHookErrorDuringExecutionAttachment(message, out var attachment);

        Assert.True(parsed);
        Assert.NotNull(attachment);
        Assert.Equal("boom", attachment!.Content);
        Assert.Equal("TaskCompleted", attachment.HookName);
        Assert.Equal("tool-2", attachment.ToolUseId);
        Assert.Equal(HookEvent.TaskCompleted, attachment.HookEvent);
    }

    [Fact]
    public void TryGetHookErrorDuringExecutionAttachment_Rejects_Invalid_Optional_Number_Metadata()
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
                        ["attachmentType"] = "hook_error_during_execution",
                        ["content"] = "boom",
                        ["hookName"] = "Stop",
                        ["toolUseID"] = "tool-3",
                        ["hookEvent"] = "Stop",
                        ["durationMs"] = "bad-number"
                    })
            ],
            DateTimeOffset.UtcNow);

        var parsed = QueryAttachmentHelpers.TryGetHookErrorDuringExecutionAttachment(message, out var attachment);

        Assert.False(parsed);
        Assert.Null(attachment);
    }
}
