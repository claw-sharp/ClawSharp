// TS origin: ./commands/tasks/tasks.tsx, ./components/tasks/BackgroundTasksDialog.tsx, ./components/tasks/BackgroundTask.tsx
using ClawSharp.Core;
using ClawSharp.Tasks;
using ClawSharp.Ui.Terminal;

namespace ClawSharp.UnitTests;

public class BackgroundTasksCommandRendererTests
{
    [Fact]
    public void RenderList_Returns_Empty_State_When_No_Background_Tasks()
    {
        var renderer = new BackgroundTasksCommandRenderer();

        var rendered = renderer.RenderList(new Dictionary<string, ClawSharpTask>(StringComparer.Ordinal));

        Assert.Equal("No background tasks.", rendered);
    }

    [Fact]
    public void RenderList_Groups_Tasks_In_Ts_Order()
    {
        var renderer = new BackgroundTasksCommandRenderer();
        var tasks = new Dictionary<string, ClawSharpTask>(StringComparer.Ordinal)
        {
            ["agent-1"] = new LocalAgentTask(
                "agent-1",
                "Review diff",
                ClawSharp.Tasks.TaskStatus.Running,
                DateTimeOffset.UtcNow.AddMinutes(-1),
                "agent.log",
                "Review the diff",
                "reviewer"),
            ["bash-1"] = new LocalBashTask(
                "bash-1",
                "Run build",
                ClawSharp.Tasks.TaskStatus.Running,
                DateTimeOffset.UtcNow,
                "bash.log",
                "dotnet build"),
            ["remote-1"] = new RemoteAgentTask(
                "remote-1",
                "Cloud review",
                ClawSharp.Tasks.TaskStatus.Pending,
                DateTimeOffset.UtcNow.AddMinutes(-2),
                "remote.log",
                "session-1",
                "remote-control",
                "PR review")
        };

        var rendered = renderer.RenderList(tasks);

        Assert.Contains("1 shell", rendered, StringComparison.Ordinal);
        Assert.Contains("1 remote agent", rendered, StringComparison.Ordinal);
        Assert.Contains("1 agent", rendered, StringComparison.Ordinal);
        Assert.Contains("shells", rendered, StringComparison.Ordinal);
        Assert.Contains("remote agents", rendered, StringComparison.Ordinal);
        Assert.Contains("agents", rendered, StringComparison.Ordinal);
        Assert.Contains("Use /tasks <task-id> to inspect a task.", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderList_Excludes_Foreground_Local_Agent_Tasks()
    {
        var renderer = new BackgroundTasksCommandRenderer();
        var tasks = new Dictionary<string, ClawSharpTask>(StringComparer.Ordinal)
        {
            ["agent-foreground"] = new LocalAgentTask(
                "agent-foreground",
                "Foreground review",
                ClawSharp.Tasks.TaskStatus.Running,
                DateTimeOffset.UtcNow,
                "agent.log",
                "Review the diff",
                "reviewer",
                IsBackgrounded: false)
        };

        var rendered = renderer.RenderList(tasks);

        Assert.Equal("No background tasks.", rendered);
    }

    [Fact]
    public void RenderDetail_Includes_Type_Specific_Metadata_And_Output()
    {
        var renderer = new BackgroundTasksCommandRenderer();
        var task = new LocalBashTask(
            "bash-1",
            "Run build",
            ClawSharp.Tasks.TaskStatus.Completed,
            DateTimeOffset.Parse("2026-04-01T10:00:00+00:00"),
            "build.log",
            "dotnet build",
            ExitCode: 0,
            EndTime: DateTimeOffset.Parse("2026-04-01T10:02:00+00:00"));

        var rendered = renderer.RenderDetail(task, "Build succeeded\n");

        Assert.Contains("bash-1 [completed] local_bash", rendered, StringComparison.Ordinal);
        Assert.Contains("Command: dotnet build", rendered, StringComparison.Ordinal);
        Assert.Contains("Exit code: 0", rendered, StringComparison.Ordinal);
        Assert.Contains("Output", rendered, StringComparison.Ordinal);
        Assert.Contains("Build succeeded", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderDetail_Includes_Agent_Progress_Metadata()
    {
        var renderer = new BackgroundTasksCommandRenderer();
        var task = new LocalAgentTask(
            "agent-1",
            "Review diff",
            ClawSharp.Tasks.TaskStatus.Running,
            DateTimeOffset.Parse("2026-04-01T10:00:00+00:00"),
            "agent.log",
            "Review the diff",
            "reviewer",
            Model: "gpt-5.4",
            WorktreePath: "D:\\repo\\.worktrees\\agent-1",
            WorktreeBranch: "agent/agent-1",
            Progress: new AgentProgress(
                3,
                1200,
                new ToolActivity(
                    "Read",
                    new Dictionary<string, object?> { ["file_path"] = "src/App.tsx" },
                    "Reading src/App.tsx"),
                [
                    new ToolActivity(
                        "Glob",
                        new Dictionary<string, object?> { ["path"] = "src" },
                        "Searching for task renderers",
                        IsSearch: true),
                    new ToolActivity(
                        "Read",
                        new Dictionary<string, object?> { ["file_path"] = "src/App.tsx" },
                        "Reading src/App.tsx",
                        IsRead: true)
                ],
                Summary: "Inspecting relevant files"));

        var rendered = renderer.RenderDetail(task, "assistant transcript\n");

        Assert.Contains("reviewer › Review diff", rendered, StringComparison.Ordinal);
        Assert.Contains("1.2k tokens", rendered, StringComparison.Ordinal);
        Assert.Contains("3 tools", rendered, StringComparison.Ordinal);
        Assert.Contains("Progress", rendered, StringComparison.Ordinal);
        Assert.Contains("Searching for task renderers", rendered, StringComparison.Ordinal);
        Assert.Contains("› Reading src/App.tsx", rendered, StringComparison.Ordinal);
        Assert.Contains("Prompt", rendered, StringComparison.Ordinal);
        Assert.Contains("Review the diff", rendered, StringComparison.Ordinal);
        Assert.Contains("Output", rendered, StringComparison.Ordinal);
        Assert.Contains("assistant transcript", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderDetail_Uses_Plan_Content_For_Local_Agent_When_Present()
    {
        var renderer = new BackgroundTasksCommandRenderer();
        var task = new LocalAgentTask(
            "agent-1",
            "Review diff",
            ClawSharp.Tasks.TaskStatus.Running,
            DateTimeOffset.Parse("2026-04-01T10:00:00+00:00"),
            "agent.log",
            "<plan>1. inspect\n2. report</plan>",
            "reviewer");

        var rendered = renderer.RenderDetail(task, null);

        Assert.Contains("Plan", rendered, StringComparison.Ordinal);
        Assert.Contains("1. inspect", rendered, StringComparison.Ordinal);
        Assert.Contains("2. report", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("Prompt", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderDetail_Includes_InProcessTeammate_View_Sections()
    {
        var renderer = new BackgroundTasksCommandRenderer();
        var task = new InProcessTeammateTask(
            "task-1",
            "researcher: inspect repo",
            ClawSharp.Tasks.TaskStatus.Running,
            DateTimeOffset.Parse("2026-04-01T10:00:00+00:00"),
            "teammate.log",
            new TeammateIdentity("researcher@alpha", "researcher", "alpha", PlanModeRequired: true, ParentSessionId: "session-1", Color: "blue"),
            "inspect repo",
            Model: "gpt-5.4",
            PermissionMode: PermissionMode.Plan,
            PendingUserMessages: ["continue searching"],
            Progress: new AgentProgress(
                2,
                1200,
                new ToolActivity(
                    "Read",
                    new Dictionary<string, object?> { ["file_path"] = "README.md" },
                    "Reading README.md",
                    IsRead: true),
                [
                    new ToolActivity(
                        "Grep",
                        new Dictionary<string, object?> { ["pattern"] = "ClawSharp" },
                        "Searching for ClawSharp",
                        IsSearch: true),
                    new ToolActivity(
                        "Read",
                        new Dictionary<string, object?> { ["file_path"] = "README.md" },
                        "Reading README.md",
                        IsRead: true)
                ]),
            ShutdownRequested: true);

        var rendered = renderer.RenderDetail(task, "teammate transcript\n");

        Assert.Contains("@researcher (stopping)", rendered, StringComparison.Ordinal);
        Assert.Contains("1.2k tokens", rendered, StringComparison.Ordinal);
        Assert.Contains("2 tools", rendered, StringComparison.Ordinal);
        Assert.Contains("Progress", rendered, StringComparison.Ordinal);
        Assert.Contains("Searching for ClawSharp", rendered, StringComparison.Ordinal);
        Assert.Contains("› Reading README.md", rendered, StringComparison.Ordinal);
        Assert.Contains("Prompt", rendered, StringComparison.Ordinal);
        Assert.Contains("inspect repo", rendered, StringComparison.Ordinal);
        Assert.Contains("Output", rendered, StringComparison.Ordinal);
        Assert.Contains("teammate transcript", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderDetail_Includes_Failed_Teammate_Error_Block()
    {
        var renderer = new BackgroundTasksCommandRenderer();
        var task = new InProcessTeammateTask(
            "task-1",
            "researcher: inspect repo",
            ClawSharp.Tasks.TaskStatus.Failed,
            DateTimeOffset.Parse("2026-04-01T10:00:00+00:00"),
            "teammate.log",
            new TeammateIdentity("researcher@alpha", "researcher", "alpha", PlanModeRequired: true, ParentSessionId: "session-1", Color: "blue"),
            "inspect repo",
            Error: "boom");

        var rendered = renderer.RenderDetail(task, null);

        Assert.Contains("Failed", rendered, StringComparison.Ordinal);
        Assert.Contains("Error", rendered, StringComparison.Ordinal);
        Assert.Contains("boom", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderList_Uses_Teammate_Activity_Label()
    {
        var renderer = new BackgroundTasksCommandRenderer();
        var tasks = new Dictionary<string, ClawSharpTask>(StringComparer.Ordinal)
        {
            ["task-1"] = new InProcessTeammateTask(
                "task-1",
                "researcher: inspect repo",
                ClawSharp.Tasks.TaskStatus.Running,
                DateTimeOffset.UtcNow,
                "teammate.log",
                new TeammateIdentity("researcher@alpha", "researcher", "alpha", PlanModeRequired: true, ParentSessionId: "session-1", Color: "blue"),
                "inspect repo",
                Progress: new AgentProgress(
                    2,
                    0,
                    RecentActivities:
                    [
                        new ToolActivity(
                            "Grep",
                            new Dictionary<string, object?>(),
                            "Searching for ClawSharp",
                            IsSearch: true),
                        new ToolActivity(
                            "Read",
                            new Dictionary<string, object?>(),
                            "Reading README.md",
                            IsRead: true)
                    ]))
        };

        var rendered = renderer.RenderList(tasks);

        Assert.Contains("@researcher: Searching for 1 pattern, reading 1 file…", rendered, StringComparison.Ordinal);
    }
}
