// TS origin: ./tasks/stopTask.ts, ./tasks/LocalShellTask/killShellTasks.ts, ./tasks/LocalShellTask/LocalShellTask.tsx
using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Tasks;
using ClawSharp.Tools;

namespace ClawSharp.UnitTests;

public sealed class TaskRuntimeParityTests
{
    [Fact]
    public async Task TaskStopTool_Stops_Background_Bash_Task_Without_Completion_Notification()
    {
        var tempDir = CreateTempDirectory("clawsharp-task-stop-tests");
        try
        {
            var shellPath = await BashShellDetection.FindSuitableShellAsync();
            if (string.IsNullOrWhiteSpace(shellPath))
            {
                return;
            }

            var queue = new InMemoryQueuedCommandQueue();
            var tasks = new TaskRegistry(tempDir, queuedCommandQueue: queue);
            var registry = new ToolRegistry(
                tempDir,
                tasks,
                toolPermissionContext: CreatePermissionContext(
                    mode: PermissionMode.BypassPermissions,
                    isBypassPermissionsModeAvailable: true));
            var session = new DefaultSessionFactory(tempDir).Create();

            var backgroundResult = await registry.ExecuteAsync(
                "Bash",
                """{"command":"sleep 5","description":"Long sleep","run_in_background":true}""",
                session,
                new ClawSharpSettings());
            var taskId = Assert.IsType<JsonObject>(backgroundResult.StructuredOutput)["backgroundTaskId"]?.GetValue<string>();
            Assert.False(string.IsNullOrWhiteSpace(taskId));

            var stopResult = await registry.ExecuteAsync(
                "TaskStop",
                $$"""{"task_id":"{{taskId}}"}""",
                session,
                new ClawSharpSettings());

            Assert.True(stopResult.Success, stopResult.Output);
            Assert.NotNull(await WaitForTaskStatusAsync(tasks, taskId!, ClawSharp.Tasks.TaskStatus.Killed));

            await Task.Delay(300);
            Assert.Null(queue.Dequeue());
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task TaskRegistry_StallWatchdog_Enqueues_Interactive_Prompt_Notification()
    {
        var tempDir = CreateTempDirectory("clawsharp-stall-watchdog-tests");
        try
        {
            var queue = new InMemoryQueuedCommandQueue();
            var tasks = new TaskRegistry(
                tempDir,
                queuedCommandQueue: queue,
                stallCheckInterval: TimeSpan.FromMilliseconds(25),
                stallThreshold: TimeSpan.FromMilliseconds(60),
                stallTailBytes: 256);
            var session = new DefaultSessionFactory(tempDir).Create();
            var task = await tasks.CreateLocalBashForSessionAsync(
                session.Id,
                "Install dependency",
                "npm install",
                ClawSharp.Tasks.TaskStatus.Running,
                isBackgrounded: true);

            await tasks.AppendOutputAsync(task.Id, "Proceed? (y/n)\n");
            Assert.True(tasks.TryStartLocalBashStallWatchdog(task.Id));

            var notification = await WaitForNotificationAsync(queue);
            Assert.NotNull(notification);
            Assert.Contains("appears to be waiting for interactive input", notification!, StringComparison.Ordinal);
            Assert.Contains("Proceed? (y/n)", notification, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static ToolPermissionContext CreatePermissionContext(
        PermissionMode mode = PermissionMode.Default,
        bool isBypassPermissionsModeAvailable = false)
    {
        return new ToolPermissionContext(
            mode,
            new Dictionary<string, AdditionalWorkingDirectory>(StringComparer.Ordinal),
            new Dictionary<PermissionRuleSource, IReadOnlyList<string>>(),
            new Dictionary<PermissionRuleSource, IReadOnlyList<string>>(),
            new Dictionary<PermissionRuleSource, IReadOnlyList<string>>(),
            IsBypassPermissionsModeAvailable: isBypassPermissionsModeAvailable);
    }

    private static async Task<ClawSharpTask?> WaitForTaskStatusAsync(
        TaskRegistry tasks,
        string taskId,
        ClawSharp.Tasks.TaskStatus status)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            if (tasks.TryGet(taskId, out var task) && task?.Status == status)
            {
                return task;
            }

            await Task.Delay(50);
        }

        return null;
    }

    private static async Task<string?> WaitForNotificationAsync(InMemoryQueuedCommandQueue queue)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            var notification = queue.Dequeue();
            if (notification is not null)
            {
                return notification.Value;
            }

            await Task.Delay(50);
        }

        return null;
    }

    private static string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }
}
