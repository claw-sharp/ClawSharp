using ClawSharp.AgentHost.Contracts;
using ClawSharp.AgentHost.Projects;
using ClawSharp.AgentHost.Services;
using ClawSharp.AgentHost.Sessions;
using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public sealed class ThreadCatalogServiceTests
{
    [Fact]
    public async Task CreateThreadAsync_Creates_Empty_Transcript_And_GetThreadAsync_Returns_Detail()
    {
        var previousConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempRoot = Path.Combine(Path.GetTempPath(), "clawsharp-agenthost-thread-tests", Guid.NewGuid().ToString("N"));
        var configRoot = Path.Combine(tempRoot, ".clawsharp");
        var workspaceRoot = Path.Combine(tempRoot, "workspace");
        Directory.CreateDirectory(configRoot);
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", configRoot);

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
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", previousConfigDir);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GetThreadAsync_Paginates_To_Recent_Messages_And_Loads_Older_On_Demand()
    {
        var previousConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempRoot = Path.Combine(Path.GetTempPath(), "clawsharp-agenthost-thread-paging-tests", Guid.NewGuid().ToString("N"));
        var configRoot = Path.Combine(tempRoot, ".clawsharp");
        var workspaceRoot = Path.Combine(tempRoot, "workspace");
        Directory.CreateDirectory(configRoot);
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", configRoot);

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
                    Title = "Paged Thread"
                });

            var transcriptStore = new JsonlTranscriptStore();
            var session = await new DefaultSessionFactory(workspaceRoot, transcriptStore)
                .ResumeAsync(created.Thread.Thread.Id);
            Assert.NotNull(session);

            session!.Add(ChatMessageFactory.CreateText(MessageRole.User, "first"));
            session.Add(ChatMessageFactory.CreateText(MessageRole.Assistant, "second"));
            session.Add(ChatMessageFactory.CreateText(MessageRole.User, "third"));
            await transcriptStore.RecordTranscriptAsync(session, session.Messages);

            var latest = await threads.GetThreadAsync(
                new GetThreadRequest
                {
                    ProjectId = projectId,
                    ThreadId = created.Thread.Thread.Id,
                    PageSize = 2,
                });

            Assert.Equal(["second", "third"], latest.Thread.Messages.Select(message => message.Content).ToArray());
            Assert.True(latest.Thread.HasMoreMessages);
            Assert.Equal(latest.Thread.Messages[0].Id, latest.Thread.NextBeforeMessageId);

            var older = await threads.GetThreadAsync(
                new GetThreadRequest
                {
                    ProjectId = projectId,
                    ThreadId = created.Thread.Thread.Id,
                    PageSize = 2,
                    BeforeMessageId = latest.Thread.NextBeforeMessageId,
                });

            Assert.Equal(["first"], older.Thread.Messages.Select(message => message.Content).ToArray());
            Assert.False(older.Thread.HasMoreMessages);
            Assert.Null(older.Thread.NextBeforeMessageId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", previousConfigDir);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GetThreadAsync_Does_Not_Surface_Tool_Result_Messages_As_User_Transcript_Messages()
    {
        var previousConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempRoot = Path.Combine(Path.GetTempPath(), "clawsharp-agenthost-thread-toolresult-tests", Guid.NewGuid().ToString("N"));
        var configRoot = Path.Combine(tempRoot, ".clawsharp");
        var workspaceRoot = Path.Combine(tempRoot, "workspace");
        Directory.CreateDirectory(configRoot);
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", configRoot);

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
                    Title = "Tool Result Thread"
                });

            var transcriptStore = new JsonlTranscriptStore();
            var session = await new DefaultSessionFactory(workspaceRoot, transcriptStore)
                .ResumeAsync(created.Thread.Thread.Id);
            Assert.NotNull(session);

            session!.Add(ChatMessageFactory.CreateText(MessageRole.User, "push changes"));
            session.Add(ChatMessageFactory.CreateToolResult(
                "tool-push",
                "Bash",
                "To https://github.com/claw-sharp/test-clawsharp.git\n59484c4..d2943eb  main -> main"));
            session.Add(ChatMessageFactory.CreateText(MessageRole.Assistant, "Pushed."));
            await transcriptStore.RecordTranscriptAsync(session, session.Messages);

            var loaded = await threads.GetThreadAsync(
                new GetThreadRequest
                {
                    ProjectId = projectId,
                    ThreadId = created.Thread.Thread.Id,
                });

            Assert.Equal(
                ["push changes", "Pushed."],
                loaded.Thread.Messages.Select(message => message.Content).ToArray());
            Assert.DoesNotContain(
                loaded.Thread.Messages,
                message => message.Content.Contains("main -> main", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", previousConfigDir);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
