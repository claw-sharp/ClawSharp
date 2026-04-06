// TS parity status: focused C# coverage for the hook_non_blocking_error attachment helper and detection contract; live hook execution remains intentionally unported.
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public sealed class QueryHookNonBlockingErrorAttachmentContractTests
{
    [Fact]
    public void CreateHookNonBlockingErrorAttachmentMessage_Stores_Ts_Shaped_Metadata()
    {
        var message = ChatMessageFactory.CreateHookNonBlockingErrorAttachmentMessage(
            hookName: "Stop",
            stderr: "bad stderr",
            stdout: "partial stdout",
            exitCode: 2,
            toolUseId: "tool-1",
            hookEvent: HookEvent.Stop,
            command: "echo nope",
            durationMs: 456);

        Assert.Equal(MessageRole.System, message.Role);
        var block = Assert.Single(message.ContentBlocks);
        Assert.Equal(MessageContentKind.Attachment, block.Kind);
        Assert.Equal(string.Empty, block.Value);
        Assert.NotNull(block.Metadata);
        Assert.Equal("hook_non_blocking_error", block.Metadata!["attachmentType"]);
        Assert.Equal("Stop", block.Metadata["hookName"]);
        Assert.Equal("bad stderr", block.Metadata["stderr"]);
        Assert.Equal("partial stdout", block.Metadata["stdout"]);
        Assert.Equal("2", block.Metadata["exitCode"]);
        Assert.Equal("tool-1", block.Metadata["toolUseID"]);
        Assert.Equal("Stop", block.Metadata["hookEvent"]);
        Assert.Equal("echo nope", block.Metadata["command"]);
        Assert.Equal("456", block.Metadata["durationMs"]);
    }

    [Fact]
    public void TryGetHookNonBlockingErrorAttachment_Returns_Attachment_For_Ts_Shaped_Message()
    {
        var message = ChatMessageFactory.CreateHookNonBlockingErrorAttachmentMessage(
            hookName: "TaskCompleted",
            stderr: "stderr",
            stdout: string.Empty,
            exitCode: 17,
            toolUseId: "tool-2",
            hookEvent: HookEvent.TaskCompleted);

        var parsed = QueryAttachmentHelpers.TryGetHookNonBlockingErrorAttachment(message, out var attachment);

        Assert.True(parsed);
        Assert.NotNull(attachment);
        Assert.Equal("TaskCompleted", attachment!.HookName);
        Assert.Equal("stderr", attachment.Stderr);
        Assert.Equal(string.Empty, attachment.Stdout);
        Assert.Equal(17, attachment.ExitCode);
        Assert.Equal("tool-2", attachment.ToolUseId);
        Assert.Equal(HookEvent.TaskCompleted, attachment.HookEvent);
    }

    [Fact]
    public void TryGetHookNonBlockingErrorAttachment_Rejects_Invalid_Required_Number_Metadata()
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
                        ["attachmentType"] = "hook_non_blocking_error",
                        ["hookName"] = "Stop",
                        ["stderr"] = "err",
                        ["stdout"] = string.Empty,
                        ["exitCode"] = "bad-number",
                        ["toolUseID"] = "tool-3",
                        ["hookEvent"] = "Stop"
                    })
            ],
            DateTimeOffset.UtcNow);

        var parsed = QueryAttachmentHelpers.TryGetHookNonBlockingErrorAttachment(message, out var attachment);

        Assert.False(parsed);
        Assert.Null(attachment);
    }
}
