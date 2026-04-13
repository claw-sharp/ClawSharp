using ClawSharp.AgentHost.Contracts;
using ClawSharp.AgentHost.Projects;
using ClawSharp.AgentHost.Services;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public sealed class ExternalEditorServiceTests
{
    [Fact]
    public async Task OpenAsync_Uses_Project_Launch_Target_Aliases()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "clawsharp-agenthost-editor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var workspaceRoot = Path.Combine(tempRoot, "workspace");
        Directory.CreateDirectory(workspaceRoot);
        var store = new RecentProjectStore(Path.Combine(tempRoot, "recent-projects.json"));
        var projectId = "proj-1";
        await store.RecordOpenAsync(new ProjectSummaryDto(projectId, "workspace", workspaceRoot, DateTimeOffset.UtcNow, null, 0, null));

        string? launchedCommand = null;
        IReadOnlyList<string>? launchedArguments = null;
        var service = new ExternalEditorService(
            store,
            (command, arguments, _) =>
            {
                launchedCommand = command;
                launchedArguments = arguments;
                return Task.FromResult(new ExternalEditorLaunchDto(true, command, arguments, "ok"));
            });

        try
        {
            var response = await service.OpenAsync(new OpenExternalEditorRequest
            {
                Kind = "project",
                ProjectId = projectId,
                EditorCommand = "__finder__",
            });

            Assert.True(response.Launch.Launched);

            if (OperatingSystem.IsMacOS())
            {
                Assert.Equal("open", launchedCommand);
                Assert.Equal(["-a", "Finder", workspaceRoot], launchedArguments);
            }
            else if (OperatingSystem.IsWindows())
            {
                Assert.Equal("explorer", launchedCommand);
                Assert.Equal([workspaceRoot], launchedArguments);
            }
            else
            {
                Assert.Equal("xdg-open", launchedCommand);
                Assert.Equal([workspaceRoot], launchedArguments);
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
