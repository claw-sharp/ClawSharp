using ClawSharp.Core;
using ClawSharp.Tasks;

namespace ClawSharp.UnitTests;

public sealed class TaskLifecycleTransitionCoverageTests
{
    [Fact]
    public async Task TaskRegistry_Transitions_LocalAgent_From_Running_To_Backgrounded_And_Killed()
    {
        var workspaceRoot = CreateTempDirectory("clawsharp-task-lifecycle-agent");

        try
        {
            var tasks = new TaskRegistry(workspaceRoot);
            var session = new ConversationSession("session-1", workspaceRoot);
            using var cancellationSource = new CancellationTokenSource();

            var task = await tasks.CreateForegroundLocalAgentForSessionAsync(
                session.Id,
                "Review current diff",
                "Review the current diff",
                "reviewer",
                cancellationSource: cancellationSource);

            Assert.False(task.IsBackgrounded);
            Assert.Equal(ClawSharp.Tasks.TaskStatus.Running, task.Status);

            Assert.True(tasks.TryBackgroundLocalAgentTask(task.Id));
            Assert.True(tasks.TryUpdateLocalAgentSummary(task.Id, "Reading files"));
            Assert.True(tasks.TryUpdateLocalAgentLastReportedCounts(task.Id, 2, 1500));

            Assert.True(tasks.TryGet(task.Id, out var updated));
            var agentTask = Assert.IsType<LocalAgentTask>(updated);
            Assert.True(agentTask.IsBackgrounded);
            Assert.Equal("Reading files", agentTask.Progress?.Summary);
            Assert.Equal(2, agentTask.LastReportedToolCount);
            Assert.Equal(1500, agentTask.LastReportedTokenCount);

            var stopped = tasks.TryStopTask(task.Id, out var errorCode, out var taskType, out var description);

            Assert.True(stopped);
            Assert.Null(errorCode);
            Assert.Equal("local_agent", taskType);
            Assert.Equal("Review current diff", description);

            Assert.True(tasks.TryGet(task.Id, out var killed));
            var killedAgentTask = Assert.IsType<LocalAgentTask>(killed);
            Assert.Equal(ClawSharp.Tasks.TaskStatus.Killed, killedAgentTask.Status);
            Assert.NotNull(killedAgentTask.EndTime);
            Assert.Null(killedAgentTask.CancellationSource);
            Assert.NotNull(killedAgentTask.EvictAfter);
        }
        finally
        {
            DeleteDirectory(workspaceRoot);
        }
    }

    [Fact]
    public async Task TaskRegistry_Transitions_LocalBash_Task_To_Notified_And_Evictable()
    {
        var workspaceRoot = CreateTempDirectory("clawsharp-task-lifecycle-bash");

        try
        {
            var tasks = new TaskRegistry(workspaceRoot);
            var task = await tasks.CreateLocalBashForSessionAsync(
                "session-1",
                "Run build",
                "dotnet build",
                ClawSharp.Tasks.TaskStatus.Running);

            Assert.True(tasks.TryUpdateStatus(task.Id, ClawSharp.Tasks.TaskStatus.Completed));
            Assert.True(tasks.TryUpdateOutputOffset(task.Id, 24));
            Assert.True(tasks.TryMarkNotified(task.Id));

            Assert.True(tasks.TryGet(task.Id, out var completed));
            var bashTask = Assert.IsType<LocalBashTask>(completed);
            Assert.Equal(ClawSharp.Tasks.TaskStatus.Completed, bashTask.Status);
            Assert.Equal(24, bashTask.OutputOffset);
            Assert.True(bashTask.Notified);
            Assert.NotNull(bashTask.EndTime);

            var evicted = tasks.SweepEvictableTerminalTasks(DateTimeOffset.UtcNow.AddMinutes(1));

            Assert.Equal(1, evicted);
            Assert.False(tasks.TryGet(task.Id, out _));
            Assert.Empty(tasks.GetAll());
        }
        finally
        {
            DeleteDirectory(workspaceRoot);
        }
    }

    private static string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
