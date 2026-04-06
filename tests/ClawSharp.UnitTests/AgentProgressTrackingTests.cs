// TS origin: ./tasks/LocalAgentTask/LocalAgentTask.tsx
using ClawSharp.Core;
using ClawSharp.Tasks;
using TaskStatus = ClawSharp.Tasks.TaskStatus;

namespace ClawSharp.UnitTests;

public sealed class AgentProgressTrackingTests
{
    [Fact]
    public async Task TaskRegistry_Preserves_Agent_Summary_When_Updating_Progress()
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

        Assert.True(registry.TryUpdateLocalAgentSummary(task.Id, "Still reading files"));
        Assert.True(
            registry.TryUpdateLocalAgentProgress(
                task.Id,
                new AgentProgress(
                    4,
                    1800,
                    new ToolActivity(
                        "Read",
                        new Dictionary<string, object?> { ["file_path"] = "src/App.tsx" },
                        "Reading src/App.tsx"),
                    [new ToolActivity("Read", new Dictionary<string, object?>(), "Reading src/App.tsx")])));

        Assert.True(registry.TryGet(task.Id, out var updated));
        var agentTask = Assert.IsType<LocalAgentTask>(updated);
        Assert.NotNull(agentTask.Progress);
        Assert.Equal(4, agentTask.Progress!.ToolUseCount);
        Assert.Equal(1800, agentTask.Progress.TokenCount);
        Assert.Equal("Still reading files", agentTask.Progress.Summary);
        Assert.Equal("Reading src/App.tsx", agentTask.Progress.LastActivity?.ActivityDescription);
    }

    [Fact]
    public async Task TaskRegistry_Updates_Agent_Summary_Without_Losing_Current_Counts()
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

        Assert.True(
            registry.TryUpdateLocalAgentProgress(
                task.Id,
                new AgentProgress(
                    2,
                    900,
                    new ToolActivity(
                        "Grep",
                        new Dictionary<string, object?> { ["pattern"] = "TODO" },
                        "Searching for TODO"))));

        Assert.True(registry.TryUpdateLocalAgentSummary(task.Id, "Searching for related code"));
        Assert.True(registry.TryUpdateLocalAgentLastReportedCounts(task.Id, 2, 900));

        Assert.True(registry.TryGet(task.Id, out var updated));
        var agentTask = Assert.IsType<LocalAgentTask>(updated);
        Assert.NotNull(agentTask.Progress);
        Assert.Equal(2, agentTask.Progress!.ToolUseCount);
        Assert.Equal(900, agentTask.Progress.TokenCount);
        Assert.Equal("Searching for related code", agentTask.Progress.Summary);
        Assert.Equal("Searching for TODO", agentTask.Progress.LastActivity?.ActivityDescription);
        Assert.Equal(2, agentTask.LastReportedToolCount);
        Assert.Equal(900, agentTask.LastReportedTokenCount);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "clawsharp-agent-progress-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
