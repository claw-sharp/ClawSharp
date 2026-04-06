// TS origin: ./utils/attachments.ts, ./utils/messages.ts
// TS parity status: focused C# coverage for the hook_success attachment helper and detection contract; live hook execution remains intentionally unported.
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public sealed class QueryHookSuccessAttachmentContractTests
{
    [Fact]
    public void CreateHookSuccessAttachmentMessage_Stores_Ts_Shaped_Metadata()
    {
        var message = ChatMessageFactory.CreateHookSuccessAttachmentMessage(
            content: "success text",
            hookName: "SessionStart",
            toolUseId: "tool-1",
            hookEvent: HookEvent.SessionStart,
            stdout: "out",
            stderr: "err",
            exitCode: 0,
            command: "echo hi",
            durationMs: 123);

        Assert.Equal(MessageRole.System, message.Role);
        var block = Assert.Single(message.ContentBlocks);
        Assert.Equal(MessageContentKind.Attachment, block.Kind);
        Assert.Equal(string.Empty, block.Value);
        Assert.NotNull(block.Metadata);
        Assert.Equal("hook_success", block.Metadata!["attachmentType"]);
        Assert.Equal("success text", block.Metadata["content"]);
        Assert.Equal("SessionStart", block.Metadata["hookName"]);
        Assert.Equal("tool-1", block.Metadata["toolUseID"]);
        Assert.Equal("SessionStart", block.Metadata["hookEvent"]);
        Assert.Equal("out", block.Metadata["stdout"]);
        Assert.Equal("err", block.Metadata["stderr"]);
        Assert.Equal("0", block.Metadata["exitCode"]);
        Assert.Equal("echo hi", block.Metadata["command"]);
        Assert.Equal("123", block.Metadata["durationMs"]);
    }

    [Fact]
    public void TryGetHookSuccessAttachment_Returns_Attachment_For_Ts_Shaped_Message()
    {
        var message = ChatMessageFactory.CreateHookSuccessAttachmentMessage(
            content: "ok",
            hookName: "UserPromptSubmit",
            toolUseId: "tool-2",
            hookEvent: HookEvent.UserPromptSubmit,
            stdout: "stdout",
            exitCode: 0);

        var parsed = QueryAttachmentHelpers.TryGetHookSuccessAttachment(message, out var attachment);

        Assert.True(parsed);
        Assert.NotNull(attachment);
        Assert.Equal("ok", attachment!.Content);
        Assert.Equal("UserPromptSubmit", attachment.HookName);
        Assert.Equal("tool-2", attachment.ToolUseId);
        Assert.Equal(HookEvent.UserPromptSubmit, attachment.HookEvent);
        Assert.Equal("stdout", attachment.Stdout);
        Assert.Equal(0, attachment.ExitCode);
    }

    [Fact]
    public void TryGetHookSuccessAttachment_Rejects_Invalid_Optional_Number_Metadata()
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
                        ["attachmentType"] = "hook_success",
                        ["content"] = "ok",
                        ["hookName"] = "Stop",
                        ["toolUseID"] = "tool-3",
                        ["hookEvent"] = "Stop",
                        ["durationMs"] = "bad-number"
                    })
            ],
            DateTimeOffset.UtcNow);

        var parsed = QueryAttachmentHelpers.TryGetHookSuccessAttachment(message, out var attachment);

        Assert.False(parsed);
        Assert.Null(attachment);
    }
}
