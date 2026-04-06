using System.Text.Json.Nodes;
using System.Threading;
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Tasks;
using ClawSharp.Tools;

namespace ClawSharp.IntegrationTests;

public sealed class ShellAndFileOperationsIntegrationTests
{
    [Fact]
    public async Task ToolRegistry_Integrates_File_Tools_With_Shell_Reads_And_Writes()
    {
        var shellPath = await BashShellDetection.FindSuitableShellAsync();
        if (string.IsNullOrWhiteSpace(shellPath))
        {
            return;
        }

        var workspaceRoot = CreateTempDirectory("clawsharp-shell-file-integration");

        try
        {
            var permissionContext = CreateBypassPermissionContext();
            var registry = new ToolRegistry(
                workspaceRoot,
                new TaskRegistry(workspaceRoot),
                toolPermissionContext: permissionContext,
                appStateStore: CreateAppStateStore(workspaceRoot, permissionContext));
            var session = new DefaultSessionFactory(workspaceRoot).Create();
            var settings = new ClawSharpSettings();

            var writeResult = await registry.ExecuteAsync(
                "Write",
                """{"file_path":"notes.txt","content":"alpha\n"}""",
                session,
                settings);
            var shellResult = await registry.ExecuteAsync(
                "Bash",
                """{"command":"printf 'beta\n' >> notes.txt; cat notes.txt"}""",
                session,
                settings);
            var readResult = await registry.ExecuteAsync(
                "Read",
                "notes.txt",
                session,
                settings);

            Assert.True(writeResult.Success, writeResult.Output);
            Assert.True(shellResult.Success, shellResult.Output);
            Assert.True(readResult.Success, readResult.Output);
            Assert.Contains("alpha", shellResult.Output, StringComparison.Ordinal);
            Assert.Contains("beta", shellResult.Output, StringComparison.Ordinal);
            Assert.Equal("alpha\nbeta\n", readResult.Output);
        }
        finally
        {
            DeleteDirectory(workspaceRoot);
        }
    }

    [Fact]
    public async Task Bash_Background_TaskOutput_Integration_Returns_Final_Task_Output()
    {
        var shellPath = await BashShellDetection.FindSuitableShellAsync();
        if (string.IsNullOrWhiteSpace(shellPath))
        {
            return;
        }

        var workspaceRoot = CreateTempDirectory("clawsharp-shell-taskoutput-integration");

        try
        {
            var tasks = new TaskRegistry(workspaceRoot);
            var permissionContext = CreateBypassPermissionContext();
            var registry = new ToolRegistry(
                workspaceRoot,
                tasks,
                toolPermissionContext: permissionContext,
                appStateStore: CreateAppStateStore(workspaceRoot, permissionContext));
            var session = new DefaultSessionFactory(workspaceRoot).Create();
            var settings = new ClawSharpSettings();

            var backgroundResult = await registry.ExecuteAsync(
                "Bash",
                """{"command":"sleep 1; printf 'done\n'","description":"background integration","run_in_background":true}""",
                session,
                settings);

            Assert.True(backgroundResult.Success, backgroundResult.Output);
            var backgroundTaskId = Assert.IsType<JsonObject>(backgroundResult.StructuredOutput)["backgroundTaskId"]?.GetValue<string>();
            Assert.False(string.IsNullOrWhiteSpace(backgroundTaskId));

            var taskOutputResult = await registry.ExecuteAsync(
                "TaskOutput",
                $$"""{"task_id":"{{backgroundTaskId}}","timeout":4000}""",
                session,
                settings);

            Assert.True(taskOutputResult.Success, taskOutputResult.Output);
            var structured = Assert.IsType<JsonObject>(taskOutputResult.StructuredOutput);
            Assert.Equal("success", structured["retrieval_status"]?.GetValue<string>());
            Assert.Contains("done", structured["task"]?["output"]?.GetValue<string>(), StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(workspaceRoot);
        }
    }


    private static ClawSharpAppStateStore CreateAppStateStore(string workspaceRoot, ToolPermissionContext permissionContext)
    {
        return new ClawSharpAppStateStore(
            ClawSharpAppState.CreateDefault(
                workspaceRoot,
                StartupEnvironment.Capture(),
                new ClawSharpSettings(),
                [],
                [],
                [],
                [],
                [],
                [],
                permissionContext));
    }

    private static ToolPermissionContext CreateBypassPermissionContext()
    {
        return new ToolPermissionContext(
            PermissionMode.BypassPermissions,
            new Dictionary<string, AdditionalWorkingDirectory>(StringComparer.Ordinal),
            new Dictionary<PermissionRuleSource, IReadOnlyList<string>>(),
            new Dictionary<PermissionRuleSource, IReadOnlyList<string>>(),
            new Dictionary<PermissionRuleSource, IReadOnlyList<string>>(),
            IsBypassPermissionsModeAvailable: true);
    }

    private static string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
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
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }
}
