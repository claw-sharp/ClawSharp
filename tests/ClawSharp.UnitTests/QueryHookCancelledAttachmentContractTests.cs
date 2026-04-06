// TS parity status: focused C# coverage for the hook_cancelled attachment helper and detection contract; live hook execution remains intentionally unported.
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public sealed class QueryHookCancelledAttachmentContractTests
{
    [Fact]
    public void CreateHookCancelledAttachmentMessage_Stores_Ts_Shaped_Metadata()
    {
        var message = ChatMessageFactory.CreateHookCancelledAttachmentMessage(
            hookName: "Stop",
            toolUseId: "tool-1",
            hookEvent: HookEvent.Stop,
            command: "echo hi",
            durationMs: 123);

        Assert.Equal(MessageRole.System, message.Role);
        var block = Assert.Single(message.ContentBlocks);
        Assert.Equal(MessageContentKind.Attachment, block.Kind);
        Assert.Equal(string.Empty, block.Value);
        Assert.NotNull(block.Metadata);
        Assert.Equal("hook_cancelled", block.Metadata!["attachmentType"]);
        Assert.Equal("Stop", block.Metadata["hookName"]);
        Assert.Equal("tool-1", block.Metadata["toolUseID"]);
        Assert.Equal("Stop", block.Metadata["hookEvent"]);
        Assert.Equal("echo hi", block.Metadata["command"]);
        Assert.Equal("123", block.Metadata["durationMs"]);
    }

    [Fact]
    public void TryGetHookCancelledAttachment_Returns_Attachment_For_Ts_Shaped_Message()
    {
        var message = ChatMessageFactory.CreateHookCancelledAttachmentMessage(
            hookName: "TaskCompleted",
            toolUseId: "tool-2",
            hookEvent: HookEvent.TaskCompleted,
            durationMs: 45);

        var parsed = QueryAttachmentHelpers.TryGetHookCancelledAttachment(message, out var attachment);

        Assert.True(parsed);
        Assert.NotNull(attachment);
        Assert.Equal("TaskCompleted", attachment!.HookName);
        Assert.Equal("tool-2", attachment.ToolUseId);
        Assert.Equal(HookEvent.TaskCompleted, attachment.HookEvent);
        Assert.Equal(45, attachment.DurationMs);
    }

    [Fact]
    public void TryGetHookCancelledAttachment_Rejects_Invalid_Optional_Number_Metadata()
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
                        ["attachmentType"] = "hook_cancelled",
                        ["hookName"] = "Stop",
                        ["toolUseID"] = "tool-3",
                        ["hookEvent"] = "Stop",
                        ["durationMs"] = "bad-number"
                    })
            ],
            DateTimeOffset.UtcNow);

        var parsed = QueryAttachmentHelpers.TryGetHookCancelledAttachment(message, out var attachment);

        Assert.False(parsed);
        Assert.Null(attachment);
    }
}
