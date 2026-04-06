// TS origin: ./tasks/LocalAgentTask/LocalAgentTask.tsx
using ClawSharp.Core;
using ClawSharp.Tasks;
using TaskStatus = ClawSharp.Tasks.TaskStatus;

namespace ClawSharp.UnitTests;

public sealed class AgentForegroundTaskStateTests
{
    [Fact]
    public async Task TaskRegistry_Creates_Foreground_Local_Agent_State()
    {
        var tempDir = CreateTempDirectory();
        var session = new DefaultSessionFactory(tempDir).Create();
        var registry = new TaskRegistry(tempDir);

        var task = await registry.CreateForegroundLocalAgentForSessionAsync(
            session.Id,
            "Review diff",
            "review the diff",
            "reviewer");

        Assert.False(task.IsBackgrounded);
        Assert.Equal(TaskStatus.Running, task.Status);
    }

    [Fact]
    public async Task TaskRegistry_Backgrounds_Foreground_Local_Agent_State()
    {
        var tempDir = CreateTempDirectory();
        var session = new DefaultSessionFactory(tempDir).Create();
        var registry = new TaskRegistry(tempDir);
        var task = await registry.CreateForegroundLocalAgentForSessionAsync(
            session.Id,
            "Review diff",
            "review the diff",
            "reviewer");

        Assert.True(registry.TryBackgroundLocalAgentTask(task.Id));
        Assert.True(registry.TryGet(task.Id, out var updated));
        var agentTask = Assert.IsType<LocalAgentTask>(updated);
        Assert.True(agentTask.IsBackgrounded);
    }

    [Fact]
    public async Task TaskRegistry_Does_Not_Background_Already_Backgrounded_Local_Agent()
    {
        var tempDir = CreateTempDirectory();
        var session = new DefaultSessionFactory(tempDir).Create();
        var registry = new TaskRegistry(tempDir);
        var task = await registry.CreateLocalAgentForSessionAsync(
            session.Id,
            "Review diff",
            "review the diff",
            "reviewer",
            TaskStatus.Running);

        Assert.False(registry.TryBackgroundLocalAgentTask(task.Id));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "clawsharp-agent-foreground-state-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
