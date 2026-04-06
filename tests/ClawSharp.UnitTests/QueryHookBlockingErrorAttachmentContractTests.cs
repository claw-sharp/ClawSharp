// TS origin: ./utils/attachments.ts, ./types/hooks.ts, ./utils/messages.ts
// TS parity status: focused C# coverage for the hook_blocking_error attachment helper and detection contract; live hook execution remains intentionally unported.
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public sealed class QueryHookBlockingErrorAttachmentContractTests
{
    [Fact]
    public void CreateHookBlockingErrorAttachmentMessage_Stores_Ts_Shaped_Metadata()
    {
        var message = ChatMessageFactory.CreateHookBlockingErrorAttachmentMessage(
            new HookBlockingError("blocked by policy", "echo nope"),
            hookName: "Stop",
            toolUseId: "tool-1",
            hookEvent: HookEvent.Stop);

        Assert.Equal(MessageRole.System, message.Role);
        var block = Assert.Single(message.ContentBlocks);
        Assert.Equal(MessageContentKind.Attachment, block.Kind);
        Assert.Equal(string.Empty, block.Value);
        Assert.NotNull(block.Metadata);
        Assert.Equal("hook_blocking_error", block.Metadata!["attachmentType"]);
        Assert.Equal("""{"BlockingError":"blocked by policy","Command":"echo nope"}""", block.Metadata["blockingError"]);
        Assert.Equal("Stop", block.Metadata["hookName"]);
        Assert.Equal("tool-1", block.Metadata["toolUseID"]);
        Assert.Equal("Stop", block.Metadata["hookEvent"]);
    }

    [Fact]
    public void TryGetHookBlockingErrorAttachment_Returns_Attachment_For_Ts_Shaped_Message()
    {
        var message = ChatMessageFactory.CreateHookBlockingErrorAttachmentMessage(
            new HookBlockingError("blocked", "cmd"),
            hookName: "TaskCompleted",
            toolUseId: "tool-2",
            hookEvent: HookEvent.TaskCompleted);

        var parsed = QueryAttachmentHelpers.TryGetHookBlockingErrorAttachment(message, out var attachment);

        Assert.True(parsed);
        Assert.NotNull(attachment);
        Assert.Equal("blocked", attachment!.BlockingError.BlockingError);
        Assert.Equal("cmd", attachment.BlockingError.Command);
        Assert.Equal("TaskCompleted", attachment.HookName);
        Assert.Equal("tool-2", attachment.ToolUseId);
        Assert.Equal(HookEvent.TaskCompleted, attachment.HookEvent);
    }

    [Fact]
    public void TryGetHookBlockingErrorAttachment_Rejects_Invalid_Blocking_Error_Metadata()
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
                        ["attachmentType"] = "hook_blocking_error",
                        ["blockingError"] = "{bad-json",
                        ["hookName"] = "Stop",
                        ["toolUseID"] = "tool-3",
                        ["hookEvent"] = "Stop"
                    })
            ],
            DateTimeOffset.UtcNow);

        var parsed = QueryAttachmentHelpers.TryGetHookBlockingErrorAttachment(message, out var attachment);

        Assert.False(parsed);
        Assert.Null(attachment);
    }
}
