using ClawSharp.AgentHost.Contracts;
using ClawSharp.AgentHost.Projects;
using ClawSharp.AgentHost.Services;
using ClawSharp.AgentHost.Sessions;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public sealed class ThreadCatalogServiceTests
{
    [Fact]
    public async Task CreateThreadAsync_Creates_Empty_Transcript_And_GetThreadAsync_Returns_Detail()
    {
        var previousConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var tempRoot = Path.Combine(Path.GetTempPath(), "clawsharp-agenthost-thread-tests", Guid.NewGuid().ToString("N"));
        var configRoot = Path.Combine(tempRoot, ".claude");
        var workspaceRoot = Path.Combine(tempRoot, "workspace");
        Directory.CreateDirectory(configRoot);
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configRoot);

        try
        {
            var recentProjects = new RecentProjectStore(Path.Combine(tempRoot, "recent-projects.json"));
            var applications = new WorkspaceApplicationRegistry();
            var threads = new ThreadCatalogService(applications, recentProjects);

            var projectId = ClawSharp.AgentHost.Mapping.DesktopContractMapper.CreateProjectId(workspaceRoot);
            await recentProjects.RecordOpenAsync(
                new ProjectSummaryDto(projectId, "workspace", workspaceRoot, DateTimeOffset.UtcNow, null, 0, null));

            var created = await threads.CreateThreadAsync(
                new CreateThreadRequest
                {
                    ProjectId = projectId,
                    Title = "Desktop Thread"
                });

            Assert.Equal("Desktop Thread", created.Thread.Thread.Title);
            Assert.True(File.Exists(created.Thread.Thread.TranscriptPath));

            var loaded = await threads.GetThreadAsync(
                new GetThreadRequest
                {
                    ProjectId = projectId,
                    ThreadId = created.Thread.Thread.Id
                });

            Assert.Equal(created.Thread.Thread.Id, loaded.Thread.Thread.Id);
            Assert.Empty(loaded.Thread.Messages);
            Assert.Equal("Desktop Thread", loaded.Thread.Thread.Title);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previousConfigDir);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
