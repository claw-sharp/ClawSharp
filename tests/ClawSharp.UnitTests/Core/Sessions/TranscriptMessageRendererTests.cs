using ClawSharp.Core;
using ClawSharp.Ui.Terminal;

namespace ClawSharp.UnitTests;

public sealed class TranscriptMessageRendererTests
{
    private readonly TranscriptMessageRenderer _renderer = new();

    [Fact]
    public void Render_UserMessage_Prefixes_Each_Line()
    {
        var message = new ChatMessage(
            "msg-1",
            MessageRole.User,
            [new MessageContentBlock(MessageContentKind.Text, "first line" + Environment.NewLine + "second line")],
            DateTimeOffset.UtcNow);

        var rendered = _renderer.Render(message);

        Assert.Equal(["> first line", "> second line"], rendered);
    }

    [Fact]
    public void Render_Visible_Attachment_Block_Is_Included()
    {
        var message = new ChatMessage(
            "msg-2",
            MessageRole.Assistant,
            [new MessageContentBlock(MessageContentKind.Attachment, "Read foo.txt (10 lines)", "foo.txt")],
            DateTimeOffset.UtcNow);

        var rendered = _renderer.Render(message);

        Assert.Equal(["* Read foo.txt (10 lines)"], rendered);
    }

    [Fact]
    public void Render_Pure_ToolUse_Message_Is_Hidden()
    {
        var message = new ChatMessage(
            "msg-3",
            MessageRole.Assistant,
            [new MessageContentBlock(
                MessageContentKind.ToolUse,
                "{\"file_path\":\"foo.txt\"}",
                "Read",
                new Dictionary<string, string> { ["toolUseId"] = "tool-1" })],
            DateTimeOffset.UtcNow);

        var rendered = _renderer.Render(message);

        Assert.Empty(rendered);
    }

    [Fact]
    public void Render_Mixed_Message_Skips_ToolUse_And_Renders_Text()
    {
        var message = new ChatMessage(
            "msg-4",
            MessageRole.Assistant,
            [
                new MessageContentBlock(
                    MessageContentKind.ToolUse,
                    "{\"command\":\"echo hi\"}",
                    "Bash",
                    new Dictionary<string, string> { ["toolUseId"] = "tool-2" }),
                new MessageContentBlock(MessageContentKind.Text, "Finished running command.")
            ],
            DateTimeOffset.UtcNow);

        var rendered = _renderer.Render(message);

        Assert.Equal(["* Finished running command."], rendered);
    }

    [Fact]
    public void Render_TaskNotification_Message_Uses_Notification_Prefix_Instead_Of_User_Quote()
    {
        var message = new ChatMessage(
            "msg-4b",
            MessageRole.User,
            [
                new MessageContentBlock(
                    MessageContentKind.Text,
                    """
                    <task-notification>
                    <task-id>task-123</task-id>
                    <status>completed</status>
                    <summary>Background command "run build" completed</summary>
                    </task-notification>
                    """)
            ],
            DateTimeOffset.UtcNow);

        var rendered = _renderer.Render(message);

        Assert.Equal(["! \u25CF Background command \"run build\" completed"], rendered);
    }

    [Fact]
    public void Render_AssistantContinuation_Uses_Indented_Group_Style()
    {
        var message = new ChatMessage(
            "msg-5",
            MessageRole.Assistant,
            [new MessageContentBlock(MessageContentKind.Text, "continued answer")],
            DateTimeOffset.UtcNow);

        var rendered = _renderer.Render(message, MessageRole.Assistant);

        Assert.Equal(["  continued answer"], rendered);
    }

    [Fact]
    public void Render_Role_Transition_Inserts_Blank_Line_Between_Groups()
    {
        var message = new ChatMessage(
            "msg-6",
            MessageRole.Assistant,
            [new MessageContentBlock(MessageContentKind.Text, "new answer")],
            DateTimeOffset.UtcNow);

        var rendered = _renderer.Render(message, MessageRole.User);

        Assert.Equal(["", "* new answer"], rendered);
    }

    [Fact]
    public void Render_SystemMessage_Uses_System_Prefix()
    {
        var message = new ChatMessage(
            "msg-7",
            MessageRole.System,
            [new MessageContentBlock(MessageContentKind.Text, "Saved session")],
            DateTimeOffset.UtcNow);

        var rendered = _renderer.Render(message);

        Assert.Equal(["! Saved session"], rendered);
    }
}
