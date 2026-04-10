using ClawSharp.AgentHost.Contracts;
using ClawSharp.AgentHost.Mapping;
using ClawSharp.AgentHost.Projects;
using ClawSharp.AgentHost.Providers;
using ClawSharp.AgentHost.Services;
using ClawSharp.Core;
using ClawSharp.Infrastructure;

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

    [Fact]
    public async Task GetSettingsAsync_Without_ProjectId_Reads_Global_User_Settings()
    {
        var fixture = await ProviderCatalogFixture.CreateAsync();

        try
        {
            Directory.CreateDirectory(Path.Combine(fixture.WorkspaceRoot, ".clawsharp"));
            await File.WriteAllTextAsync(
                ClaudeConfigPaths.GetProjectSettingsFilePath(fixture.WorkspaceRoot),
                """
                {
                  "runtime": {
                    "model": "project-model"
                  }
                }
                """);

            await fixture.Service.UpdateSettingsAsync(new UpdateSettingsRequest
            {
                Provider = "openai",
                Model = "gpt-4o"
            });

            var response = await fixture.Service.GetSettingsAsync(new GetSettingsRequest());

            Assert.Equal("openai", response.Settings.Provider);
            Assert.Equal("gpt-4o", response.Settings.Model);
            Assert.Equal(ClaudeConfigPaths.GetUserSettingsFilePath(), response.Settings.ConfigPath);
            Assert.Empty(response.Settings.SettingsIssues);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task UpdateSettingsAsync_Without_ProjectId_Does_Not_Persist_Project_Overrides()
    {
        var fixture = await ProviderCatalogFixture.CreateAsync();

        try
        {
            Directory.CreateDirectory(Path.Combine(fixture.WorkspaceRoot, ".clawsharp"));
            await File.WriteAllTextAsync(
                ClaudeConfigPaths.GetProjectSettingsFilePath(fixture.WorkspaceRoot),
                """
                {
                  "runtime": {
                    "permissionMode": "plan",
                    "enableTelemetry": false
                  }
                }
                """);

            await fixture.Service.UpdateSettingsAsync(new UpdateSettingsRequest
            {
                Provider = "openai",
                Model = "gpt-4o"
            });

            var stored = await new JsonSettingsStore(ClaudeConfigPaths.GetUserSettingsFilePath()).LoadAsync();

            Assert.Equal("gpt-4o", stored.Runtime.Model);
            Assert.Equal(PermissionMode.Default, stored.Runtime.PermissionMode);
            Assert.False(stored.Runtime.EnableTelemetry);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task UpdateSettingsAsync_Can_Persist_Codex_External_Preference_Alongside_Saved_Credentials()
    {
        var fixture = await ProviderCatalogFixture.CreateAsync();

        try
        {
            await fixture.Service.UpdateSettingsAsync(new UpdateSettingsRequest
            {
                Provider = "codex",
                Model = "codexplan",
                ApiKey = "saved-codex-token",
                AccountId = "saved-account",
                UseExternalCredential = true
            });

            var stored = await new JsonSettingsStore(ClaudeConfigPaths.GetUserSettingsFilePath()).LoadAsync();
            var connection = stored.AgentModels["codexplan"];

            Assert.Equal("codex", connection.Provider);
            Assert.Equal("saved-codex-token", connection.ApiKey);
            Assert.Equal("saved-account", connection.AccountId);
            Assert.True(connection.UseExternalCredential);
            Assert.Equal("codexplan", stored.AgentRouting["default"]);
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
            var previousConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
            var root = Path.Combine(Path.GetTempPath(), "clawsharp-agenthost-provider-tests", Guid.NewGuid().ToString("N"));
            var configRoot = Path.Combine(root, ".clawsharp");
            var workspaceRoot = Path.Combine(root, "repo");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(configRoot);
            Directory.CreateDirectory(workspaceRoot);
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", configRoot);

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
