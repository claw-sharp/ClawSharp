using ClawSharp.AgentHost.Contracts;
using ClawSharp.AgentHost.Mapping;
using ClawSharp.AgentHost.Projects;
using ClawSharp.AgentHost.Providers;
using ClawSharp.AgentHost.Services;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public sealed class ProviderCatalogServiceTests
{
    [Fact]
    public async Task GetSettingsAsync_Reports_When_Any_Provider_Credential_Is_Configured()
    {
        var fixture = await ProviderCatalogFixture.CreateAsync();

        try
        {
            await fixture.Service.UpdateSettingsAsync(new UpdateSettingsRequest
            {
                ProjectId = fixture.ProjectId,
                Provider = "openai",
                Model = "gpt-4o",
                ApiKey = "sk-openai-test"
            });

            var response = await fixture.Service.GetSettingsAsync(new GetSettingsRequest
            {
                ProjectId = fixture.ProjectId
            });

            Assert.True(response.Settings.HasAnyConfiguredProviderCredential);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task ValidateProviderConfigAsync_Uses_Live_Validation_When_Requested()
    {
        var liveValidationService = new RecordingProviderLiveValidationService();
        var fixture = await ProviderCatalogFixture.CreateAsync(liveValidationService);

        try
        {
            await fixture.Service.UpdateSettingsAsync(new UpdateSettingsRequest
            {
                ProjectId = fixture.ProjectId,
                Provider = "openai",
                Model = "gpt-4o",
                ApiKey = "sk-openai-test"
            });

            var response = await fixture.Service.ValidateProviderConfigAsync(new ValidateProviderConfigRequest
            {
                ProjectId = fixture.ProjectId,
                Provider = "openai",
                Model = "gpt-4o",
                LiveCheck = true
            });

            Assert.True(liveValidationService.WasCalled);
            Assert.True(response.Validation.IsValid);
            Assert.Empty(response.Validation.Errors);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private sealed class RecordingProviderLiveValidationService : IProviderLiveValidationService
    {
        public bool WasCalled { get; private set; }

        public Task<ProviderValidationDto> ValidateAsync(
            string provider,
            string model,
            ClawSharp.Core.ClawSharpSettings settings,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(new ProviderValidationDto(provider, true, [], []));
        }
    }

    private sealed class ProviderCatalogFixture : IDisposable
    {
        private readonly string? _previousConfigDir;

        private ProviderCatalogFixture(
            string root,
            string configRoot,
            string workspaceRoot,
            string projectId,
            RecentProjectStore store,
            ProviderCatalogService service,
            string? previousConfigDir)
        {
            Root = root;
            ConfigRoot = configRoot;
            WorkspaceRoot = workspaceRoot;
            ProjectId = projectId;
            Store = store;
            Service = service;
            _previousConfigDir = previousConfigDir;
        }

        public string Root { get; }
        public string ConfigRoot { get; }
        public string WorkspaceRoot { get; }
        public string ProjectId { get; }
        public RecentProjectStore Store { get; }
        public ProviderCatalogService Service { get; }

        public static async Task<ProviderCatalogFixture> CreateAsync(
            IProviderLiveValidationService? liveValidationService = null)
        {
            var previousConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            var root = Path.Combine(Path.GetTempPath(), "clawsharp-agenthost-provider-tests", Guid.NewGuid().ToString("N"));
            var configRoot = Path.Combine(root, ".claude");
            var workspaceRoot = Path.Combine(root, "repo");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(configRoot);
            Directory.CreateDirectory(workspaceRoot);
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configRoot);

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

            var service = new ProviderCatalogService(
                new WorkspaceApplicationRegistry(),
                store,
                liveValidationService: liveValidationService);

            return new ProviderCatalogFixture(
                root,
                configRoot,
                workspaceRoot,
                projectId,
                store,
                service,
                previousConfigDir);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _previousConfigDir);
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
