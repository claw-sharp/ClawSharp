// TS origin: ./tasks/pillLabel.ts, ./components/tasks/BackgroundTaskStatus.tsx
using ClawSharp.Tasks;
using ClawSharp.Ui.Terminal;
using TaskStatus = ClawSharp.Tasks.TaskStatus;

namespace ClawSharp.UnitTests;

public sealed class BackgroundTaskSummaryRendererTests
{
    private readonly BackgroundTaskSummaryRenderer _renderer = new();

    [Fact]
    public void Render_Splits_Local_Bash_Tasks_Into_Shells_And_Monitors()
    {
        var tasks = new ClawSharpTask[]
        {
            new LocalBashTask("task-1", "shell", TaskStatus.Running, DateTimeOffset.UtcNow, "shell.log", "echo hi"),
            new LocalBashTask("task-2", "monitor", TaskStatus.Running, DateTimeOffset.UtcNow, "monitor.log", "tail", Kind: BashTaskKind.Monitor)
        };

        var rendered = _renderer.Render(tasks);

        Assert.Equal("1 shell, 1 monitor", rendered);
    }

    [Fact]
    public void Render_Uses_Cloud_Session_Label_For_Remote_Agent_Tasks()
    {
        var tasks = new ClawSharpTask[]
        {
            new RemoteAgentTask("task-1", "remote one", TaskStatus.Running, DateTimeOffset.UtcNow, "one.log", "session-1", "review", "Remote One"),
            new RemoteAgentTask("task-2", "remote two", TaskStatus.Running, DateTimeOffset.UtcNow, "two.log", "session-2", "review", "Remote Two")
        };

        var rendered = _renderer.Render(tasks);

        Assert.Equal("\u25C7 2 cloud sessions", rendered);
    }

    [Fact]
    public void Render_Falls_Back_To_Generic_Count_For_Teammate_Tasks_Without_Team_Metadata()
    {
        var tasks = new ClawSharpTask[]
        {
            new("task-1", TaskType.InProcessTeammate, "teammate one", TaskStatus.Running, DateTimeOffset.UtcNow, "one.log"),
            new("task-2", TaskType.InProcessTeammate, "teammate two", TaskStatus.Running, DateTimeOffset.UtcNow, "two.log")
        };

        var rendered = _renderer.Render(tasks);

        Assert.Equal("2 background tasks", rendered);
    }
}
