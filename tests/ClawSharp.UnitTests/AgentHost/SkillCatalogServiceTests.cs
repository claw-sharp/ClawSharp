using System.Text;
using ClawSharp.AgentHost.Contracts;
using ClawSharp.AgentHost.Ipc;
using ClawSharp.AgentHost.Mapping;
using ClawSharp.AgentHost.Projects;
using ClawSharp.AgentHost.Services;
using ClawSharp.AgentHost.Skills;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public sealed class SkillCatalogServiceTests
{
    [Fact]
    public async Task CreateSkillAsync_WritesProjectSkillAndRefreshesCatalog()
    {
        var fixture = await SkillCatalogFixture.CreateAsync();

        try
        {
            var response = await fixture.Service.CreateSkillAsync(new CreateSkillRequest
            {
                ProjectId = fixture.ProjectId,
                Name = "release-notes",
                Description = "Summarize the user-facing changes in this branch.",
                Instructions = "1. Review the merged work.\n2. Draft concise release notes."
            });

            var skillFilePath = Path.Combine(fixture.WorkspaceRoot, ".clawsharp", "skills", "release-notes", "SKILL.md");
            Assert.True(File.Exists(skillFilePath));

            var content = await File.ReadAllTextAsync(skillFilePath);
            Assert.Contains("# Release Notes", content);
            Assert.Contains("Summarize the user-facing changes in this branch.", content);
            Assert.Contains("Draft concise release notes.", content);

            Assert.Equal("release-notes", response.Skill.Name);
            Assert.Contains(response.Skills, static skill => skill.Name == "release-notes");

            var app = await fixture.ApplicationRegistry.GetOrCreateAsync(fixture.WorkspaceRoot);
            Assert.Contains(app.AppStateStore.GetState().Skills, static skill => skill.Name == "release-notes");
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task CreateSkillAsync_RejectsDuplicateSkillNames()
    {
        var fixture = await SkillCatalogFixture.CreateAsync();

        try
        {
            await fixture.Service.CreateSkillAsync(new CreateSkillRequest
            {
                ProjectId = fixture.ProjectId,
                Name = "release-notes",
                Instructions = "1. Review the merged work."
            });

            var error = await Assert.ThrowsAsync<AgentHostException>(() => fixture.Service.CreateSkillAsync(new CreateSkillRequest
            {
                ProjectId = fixture.ProjectId,
                Name = "release-notes",
                Instructions = "1. Try again."
            }));

            Assert.Equal("skill_exists", error.Code);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private sealed class SkillCatalogFixture : IDisposable
    {
        private readonly string? _previousConfigDir;

        private SkillCatalogFixture(
            string root,
            string workspaceRoot,
            string projectId,
            RecentProjectStore store,
            WorkspaceApplicationRegistry applicationRegistry,
            SkillCatalogService service,
            string? previousConfigDir)
        {
            Root = root;
            WorkspaceRoot = workspaceRoot;
            ProjectId = projectId;
            Store = store;
            ApplicationRegistry = applicationRegistry;
            Service = service;
            _previousConfigDir = previousConfigDir;
        }

        public string Root { get; }
        public string WorkspaceRoot { get; }
        public string ProjectId { get; }
        public RecentProjectStore Store { get; }
        public WorkspaceApplicationRegistry ApplicationRegistry { get; }
        public SkillCatalogService Service { get; }

        public static async Task<SkillCatalogFixture> CreateAsync()
        {
            var previousConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
            var root = Path.Combine(Path.GetTempPath(), "clawsharp-agenthost-skill-tests", Guid.NewGuid().ToString("N"));
            var configRoot = Path.Combine(root, ".clawsharp");
            var workspaceRoot = Path.Combine(root, "repo");

            Directory.CreateDirectory(workspaceRoot);
            Directory.CreateDirectory(Path.Combine(workspaceRoot, ".git"));
            Directory.CreateDirectory(configRoot);
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", configRoot);

            File.WriteAllText(Path.Combine(workspaceRoot, "README.md"), "# Test Repo", Encoding.UTF8);

            var store = new RecentProjectStore(Path.Combine(root, "recent-projects.json"));
            var projectId = DesktopContractMapper.CreateProjectId(workspaceRoot);
            await store.RecordOpenAsync(new ProjectSummaryDto(
                projectId,
                "repo",
                workspaceRoot,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                0,
                "main"));

            var applicationRegistry = new WorkspaceApplicationRegistry();
            var service = new SkillCatalogService(applicationRegistry, store);

            return new SkillCatalogFixture(
                root,
                workspaceRoot,
                projectId,
                store,
                applicationRegistry,
                service,
                previousConfigDir);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", _previousConfigDir);
            if (Directory.Exists(Root))
            {
                var attempts = 0;
                while (Directory.Exists(Root))
                {
                    try
                    {
                        Directory.Delete(Root, recursive: true);
                        break;
                    }
                    catch (IOException) when (attempts < 4)
                    {
                        attempts++;
                        Thread.Sleep(50);
                    }
                }
            }
        }
    }
}
