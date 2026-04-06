// TS origin: ./tasks/LocalAgentTask/LocalAgentTask.tsx, ./tools/AgentTool/AgentTool.tsx
using ClawSharp.Core;
using ClawSharp.Tasks;
using TaskStatus = ClawSharp.Tasks.TaskStatus;

namespace ClawSharp.UnitTests;

public sealed class AgentWorktreeStateTests
{
    [Fact]
    public async Task TaskRegistry_Creates_Local_Agent_With_Worktree_State()
    {
        var tempDir = CreateTempDirectory();
        var session = new DefaultSessionFactory(tempDir).Create();
        var registry = new TaskRegistry(tempDir);

        var task = await registry.CreateLocalAgentForSessionAsync(
            session.Id,
            "Review diff",
            "review the diff",
            "reviewer",
            TaskStatus.Running,
            worktreePath: "D:\\repo\\.worktrees\\agent-1",
            worktreeBranch: "agent/agent-1");

        Assert.Equal("D:\\repo\\.worktrees\\agent-1", task.WorktreePath);
        Assert.Equal("agent/agent-1", task.WorktreeBranch);
    }

    [Fact]
    public async Task TaskRegistry_Updates_Local_Agent_Worktree_State()
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

        Assert.True(registry.TryUpdateLocalAgentWorktree(task.Id, "D:\\repo\\.worktrees\\agent-2", "agent/agent-2"));
        Assert.True(registry.TryGet(task.Id, out var updated));
        var agentTask = Assert.IsType<LocalAgentTask>(updated);
        Assert.Equal("D:\\repo\\.worktrees\\agent-2", agentTask.WorktreePath);
        Assert.Equal("agent/agent-2", agentTask.WorktreeBranch);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "clawsharp-agent-worktree-state-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
