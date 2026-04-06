// TS origin: ./utils/task/framework.ts, ./utils/task/diskOutput.ts, ./tasks/LocalAgentTask/LocalAgentTask.tsx, ./tools/TaskOutputTool/TaskOutputTool.tsx
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Query;
using ClawSharp.Tasks;
using ClawSharp.Tools;
using TaskStatus = ClawSharp.Tasks.TaskStatus;

namespace ClawSharp.UnitTests;

public sealed class TaskEvictionParityTests
{
    [Fact]
    public async Task SweepEvictableTerminalTasks_Removes_Notified_Local_Bash_Task_And_Keeps_Output_On_Disk()
    {
        var tempDir = CreateTempDirectory();
        var tasks = new TaskRegistry(tempDir);
        var session = new DefaultSessionFactory(tempDir).Create();
        var task = await tasks.CreateLocalBashForSessionAsync(
            session.Id,
            "Run build",
            "dotnet build",
            status: TaskStatus.Completed);
        await tasks.AppendOutputAsync(task.Id, "build complete\n");
        tasks.TryUpdate(
            task.Id,
            existing => existing with
            {
                Status = TaskStatus.Completed,
                EndTime = DateTimeOffset.UtcNow,
                Notified = true
            });

        var removedCount = tasks.SweepEvictableTerminalTasks();

        Assert.Equal(1, removedCount);
        Assert.False(tasks.TryGet(task.Id, out _));
        Assert.True(File.Exists(task.OutputFile));
        Assert.Equal("build complete\n", await File.ReadAllTextAsync(task.OutputFile));
    }

    [Fact]
    public async Task SweepEvictableTerminalTasks_Retains_Local_Agent_Before_Grace_Window_Expires()
    {
        var tempDir = CreateTempDirectory();
        var tasks = new TaskRegistry(tempDir);
        var session = new DefaultSessionFactory(tempDir).Create();
        var task = await tasks.CreateLocalAgentForSessionAsync(
            session.Id,
            "Review repo",
            "Review repo",
            "reviewer",
            status: TaskStatus.Completed);
        var now = DateTimeOffset.UtcNow;
        tasks.TryUpdate(
            task.Id,
            existing => existing is LocalAgentTask agentTask
                ? agentTask with
                {
                    Status = TaskStatus.Completed,
                    EndTime = now,
                    Notified = true,
                    EvictAfter = now + TaskRegistry.PanelGracePeriod
                }
                : existing);

        var removedCount = tasks.SweepEvictableTerminalTasks(now.AddSeconds(5));

        Assert.Equal(0, removedCount);
        Assert.True(tasks.TryGet(task.Id, out var retainedTask));
        Assert.IsType<LocalAgentTask>(retainedTask);
    }

    [Fact]
    public async Task SweepEvictableTerminalTasks_Removes_Local_Agent_After_Grace_Window_Expires()
    {
        var tempDir = CreateTempDirectory();
        var tasks = new TaskRegistry(tempDir);
        var session = new DefaultSessionFactory(tempDir).Create();
        var task = await tasks.CreateLocalAgentForSessionAsync(
            session.Id,
            "Review repo",
            "Review repo",
            "reviewer",
            status: TaskStatus.Completed);
        var now = DateTimeOffset.UtcNow;
        tasks.TryUpdate(
            task.Id,
            existing => existing is LocalAgentTask agentTask
                ? agentTask with
                {
                    Status = TaskStatus.Completed,
                    EndTime = now,
                    Notified = true,
                    EvictAfter = now.AddSeconds(-1)
                }
                : existing);

        var removedCount = tasks.SweepEvictableTerminalTasks(now);

        Assert.Equal(1, removedCount);
        Assert.False(tasks.TryGet(task.Id, out _));
    }

    [Fact]
    public async Task TaskOutputTool_Read_Of_Terminal_Task_Evicts_Task_After_Output_Consumption()
    {
        var tempDir = CreateTempDirectory();
        var tasks = new TaskRegistry(tempDir);
        var registry = new ToolRegistry(tempDir, tasks);
        var session = new DefaultSessionFactory(tempDir).Create();
        var settings = new ClawSharpSettings();
        var task = await tasks.CreateLocalBashForSessionAsync(
            session.Id,
            "Run build",
            "dotnet build",
            status: TaskStatus.Completed);
        await tasks.AppendOutputAsync(task.Id, "done\n");

        var result = await registry.ExecuteAsync("TaskOutput", task.Id, session, settings);

        Assert.True(result.Success);
        Assert.Contains("done", result.Output, StringComparison.Ordinal);
        Assert.False(tasks.TryGet(task.Id, out _));
    }

    [Fact]
    public async Task QueuedTaskNotificationDrainer_DrainAsync_Evicts_Eligible_Terminal_Tasks()
    {
        var tempDir = CreateTempDirectory();
        var queue = new InMemoryQueuedCommandQueue();
        var transcriptStore = new JsonlTranscriptStore();
        var tasks = new TaskRegistry(tempDir);
        var drainer = new QueuedTaskNotificationDrainer(queue, transcriptStore, tasks);
        var session = new DefaultSessionFactory(tempDir, transcriptStore).Create();
        var task = await tasks.CreateLocalBashForSessionAsync(
            session.Id,
            "Run build",
            "dotnet build",
            status: TaskStatus.Completed);
        tasks.TryUpdate(
            task.Id,
            existing => existing with
            {
                Status = TaskStatus.Completed,
                EndTime = DateTimeOffset.UtcNow,
                Notified = true
            });
        queue.EnqueuePendingNotification(new QueuedCommand(
            $"<task-notification>\n<task-id>{task.Id}</task-id>\n<status>completed</status>\n<summary>Background command completed</summary>\n</task-notification>",
            PromptInputMode.TaskNotification));

        var drained = await drainer.DrainAsync(session);

        Assert.Single(drained);
        Assert.False(tasks.TryGet(task.Id, out _));
        Assert.Single(session.Messages);
        Assert.Contains("<task-notification>", session.Messages[0].Content, StringComparison.Ordinal);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "clawsharp-task-eviction-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
