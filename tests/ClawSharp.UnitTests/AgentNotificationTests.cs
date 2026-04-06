using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Tasks;
using ClawSharp.Ui.Terminal;
using TaskStatus = ClawSharp.Tasks.TaskStatus;

namespace ClawSharp.UnitTests;

public sealed class AgentNotificationTests
{
    [Fact]
    public async Task TaskRegistry_Uses_Explicit_Error_For_Failed_Agent_Notification()
    {
        var tempDir = CreateTempDirectory();
        var session = new DefaultSessionFactory(tempDir).Create();
        var queue = new InMemoryQueuedCommandQueue();
        var tasks = new TaskRegistry(tempDir, queuedCommandQueue: queue);

        var agentTask = await tasks.CreateLocalAgentForSessionAsync(
            session.Id,
            "agent task",
            "fix bug",
            "general-purpose",
            TaskStatus.Failed);

        Assert.True(tasks.TryEnqueueLocalAgentNotification(agentTask.Id, error: "permission denied"));

        var notification = queue.Dequeue();
        Assert.NotNull(notification);
        Assert.Contains(
            "<summary>Agent \"agent task\" failed: permission denied</summary>",
            notification!.Value,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TaskRegistry_Uses_Task_Worktree_Metadata_When_Agent_Notification_Does_Not_Provide_Overrides()
    {
        var tempDir = CreateTempDirectory();
        var session = new DefaultSessionFactory(tempDir).Create();
        var queue = new InMemoryQueuedCommandQueue();
        var tasks = new TaskRegistry(tempDir, queuedCommandQueue: queue);

        var agentTask = await tasks.CreateLocalAgentForSessionAsync(
            session.Id,
            "agent task",
            "fix bug",
            "general-purpose",
            TaskStatus.Completed,
            worktreePath: "D:\\repo\\.worktrees\\agent-task",
            worktreeBranch: "agent/agent-task");

        Assert.True(tasks.TryEnqueueLocalAgentNotification(agentTask.Id));

        var notification = queue.Dequeue();
        Assert.NotNull(notification);
        Assert.Contains("<worktree>", notification!.Value, StringComparison.Ordinal);
        Assert.Contains("<worktreePath>D:\\repo\\.worktrees\\agent-task</worktreePath>", notification.Value, StringComparison.Ordinal);
        Assert.Contains("<worktreeBranch>agent/agent-task</worktreeBranch>", notification.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void TaskNotificationMessageRenderer_Decodes_Agent_Notification_Summary()
    {
        const string content = """
            <task-notification>
            <task-id>agent-123</task-id>
            <status>failed</status>
            <summary>Agent "review &amp; fix" failed: bad &lt;state&gt;</summary>
            </task-notification>
            """;

        var rendered = new TaskNotificationMessageRenderer().TryRender(content);

        Assert.Equal("\u25CF Agent \"review & fix\" failed: bad <state>", rendered);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "clawsharp-agent-notification-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
