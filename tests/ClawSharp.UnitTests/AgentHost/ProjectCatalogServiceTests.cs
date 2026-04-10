using ClawSharp.AgentHost.Projects;
using ClawSharp.AgentHost.Services;
using ClawSharp.AgentHost.Sessions;
using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public sealed class ProjectCatalogServiceTests
{
    [Fact]
    public async Task RecordOpenAsync_Stores_And_Sorts_Recent_Projects()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "clawsharp-agenthost-project-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var store = new RecentProjectStore(Path.Combine(tempRoot, "recent-projects.json"));

        try
        {
            await store.RecordOpenAsync(
                new ClawSharp.AgentHost.Contracts.ProjectSummaryDto(
                    "p1",
                    "repo-one",
                    Path.Combine(tempRoot, "repo-one"),
                    DateTimeOffset.UtcNow.AddMinutes(-5),
                    null,
                    0,
                    null));
            await store.RecordOpenAsync(
                new ClawSharp.AgentHost.Contracts.ProjectSummaryDto(
                    "p2",
                    "repo-two",
                    Path.Combine(tempRoot, "repo-two"),
                    DateTimeOffset.UtcNow,
                    null,
                    0,
                    null));

            Directory.CreateDirectory(Path.Combine(tempRoot, "repo-one"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "repo-two"));

            var projects = await store.ListAsync();

            Assert.Equal(2, projects.Count);
            Assert.Equal("p2", projects[0].ProjectId);
            Assert.Equal("p1", projects[1].ProjectId);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ListRecentProjectsAsync_Returns_Stored_Metadata_Without_Rehydrating_Workspaces()
    {
        var previousConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var tempRoot = Path.Combine(Path.GetTempPath(), "clawsharp-agenthost-project-tests", Guid.NewGuid().ToString("N"));
        var configRoot = Path.Combine(tempRoot, ".clawsharp");
        var workspaceRoot = Path.Combine(tempRoot, "repo-one");
        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(configRoot);
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configRoot);

        var projectId = ClawSharp.AgentHost.Mapping.DesktopContractMapper.CreateProjectId(workspaceRoot);
        var lastOpenedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var lastUpdatedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var store = new RecentProjectStore(Path.Combine(tempRoot, "recent-projects.json"));
        var service = new ProjectCatalogService(
            store,
            new ThreadCatalogService(new WorkspaceApplicationRegistry(), store));

        try
        {
            await store.RecordOpenAsync(
                new ClawSharp.AgentHost.Contracts.ProjectSummaryDto(
                    projectId,
                    "repo-one",
                    workspaceRoot,
                    lastOpenedAt,
                    lastUpdatedAt,
                    7,
                    "main"));

            var response = await service.ListRecentProjectsAsync();

            var project = Assert.Single(response.Projects);
            Assert.Equal(projectId, project.Id);
            Assert.Equal("repo-one", project.Name);
            Assert.Equal(Path.GetFullPath(workspaceRoot), project.Path);
            Assert.Equal(lastOpenedAt, project.LastOpenedAt);
            Assert.Equal(lastUpdatedAt, project.LastUpdatedAt);
            Assert.Equal(7, project.ThreadCount);
            Assert.Equal("main", project.GitBranch);
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

    [Fact]
    public async Task OpenProjectAsync_Lists_Threads_From_Metadata_Without_Rehydrating_Workspace()
    {
        var previousConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var tempRoot = Path.Combine(Path.GetTempPath(), "clawsharp-agenthost-project-open-tests", Guid.NewGuid().ToString("N"));
        var configRoot = Path.Combine(tempRoot, ".clawsharp");
        var workspaceRoot = Path.Combine(tempRoot, "repo-one");
        Directory.CreateDirectory(configRoot);
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configRoot);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var sessionFactory = new DefaultSessionFactory(workspaceRoot, transcriptStore);
            var session = sessionFactory.Create();
            session.SetCustomTitle("Fast Thread");
            session.Add(ChatMessageFactory.CreateText(MessageRole.User, "hello from metadata"));
            await transcriptStore.RecordTranscriptAsync(session, session.Messages);

            var transcriptPath = session.TranscriptPath;
            var metadataPath = SessionStoragePaths.GetSessionLogMetadataPath(workspaceRoot, session.Id);
            Assert.True(File.Exists(metadataPath));

            File.Delete(transcriptPath);

            var store = new RecentProjectStore(Path.Combine(tempRoot, "recent-projects.json"));
            var threadCatalog = new ThreadCatalogService(new WorkspaceApplicationRegistry(), store);
            var service = new ProjectCatalogService(store, threadCatalog);

            var response = await service.OpenProjectAsync(new OpenProjectRequest
            {
                ProjectPath = workspaceRoot,
            });

            var thread = Assert.Single(response.Threads);
            Assert.Equal(session.Id, thread.Id);
            Assert.Equal("Fast Thread", thread.Title);
            Assert.Equal("hello from metadata", thread.Summary);
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
