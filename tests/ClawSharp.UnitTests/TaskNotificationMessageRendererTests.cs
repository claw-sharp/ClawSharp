// TS origin: ./components/messages/UserAgentNotificationMessage.tsx, ./components/messages/UserTextMessage.tsx
using ClawSharp.Ui.Terminal;

namespace ClawSharp.UnitTests;

public sealed class TaskNotificationMessageRendererTests
{
    private readonly TaskNotificationMessageRenderer _renderer = new();

    [Fact]
    public void TryRender_Returns_Bullet_Summary_For_Task_Notification_Xml()
    {
        const string content = """
            <task-notification>
            <task-id>task-123</task-id>
            <status>completed</status>
            <summary>Background command &quot;run build&quot; completed</summary>
            </task-notification>
            """;

        var rendered = _renderer.TryRender(content);

        Assert.Equal("\u25CF Background command \"run build\" completed", rendered);
    }

    [Fact]
    public void TryRender_Returns_Null_When_Summary_Is_Missing()
    {
        const string content = """
            <task-notification>
            <task-id>task-123</task-id>
            </task-notification>
            """;

        var rendered = _renderer.TryRender(content);

        Assert.Null(rendered);
    }
}
