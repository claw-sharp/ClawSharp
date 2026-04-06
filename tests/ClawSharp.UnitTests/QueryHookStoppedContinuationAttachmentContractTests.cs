// TS origin: ./query/stopHooks.ts, ./utils/attachments.ts
// TS parity status: focused C# coverage for the hook_stopped_continuation attachment helper and detection contract; live stop-hook execution remains intentionally unported.
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public sealed class QueryHookStoppedContinuationAttachmentContractTests
{
    [Fact]
    public void CreateHookStoppedContinuationAttachmentMessage_Stores_Ts_Shaped_Metadata()
    {
        var message = ChatMessageFactory.CreateHookStoppedContinuationAttachmentMessage(
            message: "Stop hook prevented continuation",
            hookName: "Stop",
            toolUseId: "tool-1",
            hookEvent: HookEvent.Stop);

        Assert.Equal(MessageRole.System, message.Role);
        var block = Assert.Single(message.ContentBlocks);
        Assert.Equal(MessageContentKind.Attachment, block.Kind);
        Assert.Equal(string.Empty, block.Value);
        Assert.NotNull(block.Metadata);
        Assert.Equal("hook_stopped_continuation", block.Metadata!["attachmentType"]);
        Assert.Equal("Stop hook prevented continuation", block.Metadata["message"]);
        Assert.Equal("Stop", block.Metadata["hookName"]);
        Assert.Equal("tool-1", block.Metadata["toolUseID"]);
        Assert.Equal("Stop", block.Metadata["hookEvent"]);
    }

    [Fact]
    public void TryGetHookStoppedContinuationAttachment_Returns_Attachment_For_Ts_Shaped_Message()
    {
        var message = ChatMessageFactory.CreateHookStoppedContinuationAttachmentMessage(
            message: "TaskCompleted hook prevented continuation",
            hookName: "TaskCompleted",
            toolUseId: "tool-2",
            hookEvent: HookEvent.TaskCompleted);

        var parsed = QueryAttachmentHelpers.TryGetHookStoppedContinuationAttachment(message, out var attachment);

        Assert.True(parsed);
        Assert.NotNull(attachment);
        Assert.Equal("TaskCompleted hook prevented continuation", attachment!.Message);
        Assert.Equal("TaskCompleted", attachment.HookName);
        Assert.Equal("tool-2", attachment.ToolUseId);
        Assert.Equal(HookEvent.TaskCompleted, attachment.HookEvent);
    }

    [Fact]
    public void TryGetHookStoppedContinuationAttachment_Rejects_Invalid_Hook_Event_Metadata()
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
                        ["attachmentType"] = "hook_stopped_continuation",
                        ["message"] = "bad hook",
                        ["hookName"] = "Stop",
                        ["toolUseID"] = "tool-3",
                        ["hookEvent"] = "NotAHookEvent"
                    })
            ],
            DateTimeOffset.UtcNow);

        var parsed = QueryAttachmentHelpers.TryGetHookStoppedContinuationAttachment(message, out var attachment);

        Assert.False(parsed);
        Assert.Null(attachment);
    }
}
