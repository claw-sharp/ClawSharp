// TS parity status: focused C# coverage for the hook_permission_decision attachment helper and detection contract; live hook execution remains intentionally unported.
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public sealed class QueryHookPermissionDecisionAttachmentContractTests
{
    [Fact]
    public void CreateHookPermissionDecisionAttachmentMessage_Stores_Ts_Shaped_Metadata()
    {
        var message = ChatMessageFactory.CreateHookPermissionDecisionAttachmentMessage(
            decision: "allow",
            toolUseId: "tool-1",
            hookEvent: HookEvent.PermissionRequest);

        Assert.Equal(MessageRole.System, message.Role);
        var block = Assert.Single(message.ContentBlocks);
        Assert.Equal(MessageContentKind.Attachment, block.Kind);
        Assert.Equal(string.Empty, block.Value);
        Assert.NotNull(block.Metadata);
        Assert.Equal("hook_permission_decision", block.Metadata!["attachmentType"]);
        Assert.Equal("allow", block.Metadata["decision"]);
        Assert.Equal("tool-1", block.Metadata["toolUseID"]);
        Assert.Equal("PermissionRequest", block.Metadata["hookEvent"]);
    }

    [Fact]
    public void TryGetHookPermissionDecisionAttachment_Returns_Attachment_For_Ts_Shaped_Message()
    {
        var message = ChatMessageFactory.CreateHookPermissionDecisionAttachmentMessage(
            decision: "deny",
            toolUseId: "tool-2",
            hookEvent: HookEvent.PermissionDenied);

        var parsed = QueryAttachmentHelpers.TryGetHookPermissionDecisionAttachment(message, out var attachment);

        Assert.True(parsed);
        Assert.NotNull(attachment);
        Assert.Equal("deny", attachment!.Decision);
        Assert.Equal("tool-2", attachment.ToolUseId);
        Assert.Equal(HookEvent.PermissionDenied, attachment.HookEvent);
    }

    [Fact]
    public void TryGetHookPermissionDecisionAttachment_Rejects_Invalid_Decision_Metadata()
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
                        ["attachmentType"] = "hook_permission_decision",
                        ["decision"] = "maybe",
                        ["toolUseID"] = "tool-3",
                        ["hookEvent"] = "PermissionRequest"
                    })
            ],
            DateTimeOffset.UtcNow);

        var parsed = QueryAttachmentHelpers.TryGetHookPermissionDecisionAttachment(message, out var attachment);

        Assert.False(parsed);
        Assert.Null(attachment);
    }
}
