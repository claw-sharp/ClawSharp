using System.Text;
using System.Text.Json;
using ClawSharp.AgentHost.Contracts;
using ClawSharp.AgentHost.Mapping;
using ClawSharp.AgentHost.Plugins;
using ClawSharp.AgentHost.Projects;
using ClawSharp.AgentHost.Services;
using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public sealed class PluginCatalogServiceTests
{
    [Fact]
    public async Task ListPluginsAsync_ReportsConfiguredSensitiveOptionsWithoutLeakingValues()
    {
        var fixture = await PluginCatalogFixture.CreateAsync();

        try
        {
            await fixture.Service.SavePluginOptionsAsync(new SavePluginOptionsRequest
            {
                ProjectId = fixture.ProjectId,
                PluginId = fixture.PluginId,
                Values = new Dictionary<string, JsonElement>
                {
                    ["workspaceRoot"] = JsonSerializer.SerializeToElement(fixture.WorkspaceRoot),
                    ["apiKey"] = JsonSerializer.SerializeToElement("secret-token")
                }
            });

            var response = await fixture.Service.ListPluginsAsync(new ListPluginsRequest
            {
                ProjectId = fixture.ProjectId
            });

            var plugin = Assert.Single(response.Plugins, plugin => plugin.PluginId == fixture.PluginId);
            var apiKey = Assert.Single(plugin.Options, static option => option.Key == "apiKey");
            var workspaceRoot = Assert.Single(plugin.Options, static option => option.Key == "workspaceRoot");

            Assert.True(apiKey.HasValue);
            Assert.True(apiKey.Sensitive);
            Assert.Null(apiKey.Value);
            Assert.Equal(fixture.WorkspaceRoot, Assert.IsType<string>(workspaceRoot.Value));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task SavePluginOptionsAsync_PreservesExistingSensitiveValuesWhenOmitted()
    {
        var fixture = await PluginCatalogFixture.CreateAsync();

        try
        {
            await fixture.Service.SavePluginOptionsAsync(new SavePluginOptionsRequest
            {
                ProjectId = fixture.ProjectId,
                PluginId = fixture.PluginId,
                Values = new Dictionary<string, JsonElement>
                {
                    ["workspaceRoot"] = JsonSerializer.SerializeToElement(fixture.WorkspaceRoot),
                    ["apiKey"] = JsonSerializer.SerializeToElement("secret-token")
                }
            });

            await fixture.Service.SavePluginOptionsAsync(new SavePluginOptionsRequest
            {
                ProjectId = fixture.ProjectId,
                PluginId = fixture.PluginId,
                Values = new Dictionary<string, JsonElement>
                {
                    ["workspaceRoot"] = JsonSerializer.SerializeToElement(Path.Combine(fixture.WorkspaceRoot, "src"))
                }
            });

            var response = await fixture.Service.ListPluginsAsync(new ListPluginsRequest
            {
                ProjectId = fixture.ProjectId
            });

            var plugin = Assert.Single(response.Plugins, plugin => plugin.PluginId == fixture.PluginId);
            var apiKey = Assert.Single(plugin.Options, static option => option.Key == "apiKey");
            var workspaceRoot = Assert.Single(plugin.Options, static option => option.Key == "workspaceRoot");

            Assert.True(apiKey.HasValue);
            Assert.Equal(Path.Combine(fixture.WorkspaceRoot, "src"), Assert.IsType<string>(workspaceRoot.Value));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task SetPluginEnabledAsync_RefreshesSkillDiscovery()
    {
        var fixture = await PluginCatalogFixture.CreateAsync();

        try
        {
            var app = await fixture.ApplicationRegistry.GetOrCreateAsync(fixture.WorkspaceRoot);
            Assert.Contains(app.AppStateStore.GetState().Skills, static skill => skill.Name == "reviewer");

            var response = await fixture.Service.SetPluginEnabledAsync(new SetPluginEnabledRequest
            {
                ProjectId = fixture.ProjectId,
                PluginId = fixture.PluginId,
                Enabled = false
            });

            Assert.False(Assert.Single(response.Plugins, plugin => plugin.PluginId == fixture.PluginId).Enabled);
            Assert.DoesNotContain(app.AppStateStore.GetState().Skills, static skill => skill.Name == "reviewer");
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task ListPluginsAsync_Includes_BuiltIn_Plugins()
    {
        var fixture = await PluginCatalogFixture.CreateAsync();

        try
        {
            var response = await fixture.Service.ListPluginsAsync(new ListPluginsRequest
            {
                ProjectId = fixture.ProjectId
            });

            Assert.Contains(response.Plugins, static plugin => plugin.PluginId == "reviewer@builtin");
            Assert.Contains(response.Plugins, static plugin => plugin.PluginId == "verification@builtin");
            Assert.Contains(response.Plugins, static plugin => plugin.PluginId == "troubleshooter@builtin");
            Assert.Contains(response.Plugins, static plugin => plugin.PluginId == "settings@builtin");
            var linear = Assert.Single(response.Plugins, static plugin => plugin.PluginId == "linear@builtin");
            Assert.False(linear.Authenticated);
            var linearServer = Assert.Single(linear.McpServers);
            Assert.Equal("linear", linearServer.Name);
            Assert.Equal("http", linearServer.Type);
            Assert.Equal("https://mcp.linear.app/mcp", linearServer.Endpoint);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task ListPluginsAsync_Reports_Authenticated_Bundled_Mcp_Plugins()
    {
        var fixture = await PluginCatalogFixture.CreateAsync();

        try
        {
            var app = await fixture.ApplicationRegistry.GetOrCreateAsync(fixture.WorkspaceRoot);
            app.McpAuthStateService.SaveOAuthEntry(
                "linear",
                new McpHttpServerConfig("https://mcp.linear.app/mcp", null, null, null),
                new McpOAuthEntry(
                    "linear",
                    "https://mcp.linear.app/mcp",
                    "access-token",
                    DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()));

            await fixture.Service.SetPluginEnabledAsync(new SetPluginEnabledRequest
            {
                ProjectId = fixture.ProjectId,
                PluginId = "linear@builtin",
                Enabled = true
            });

            var response = await fixture.Service.ListPluginsAsync(new ListPluginsRequest
            {
                ProjectId = fixture.ProjectId
            });

            var linear = Assert.Single(response.Plugins, static plugin => plugin.PluginId == "linear@builtin");
            Assert.True(linear.Authenticated);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private sealed class PluginCatalogFixture : IDisposable
    {
        private readonly string? _previousConfigDir;

        private PluginCatalogFixture(
            string root,
            string configRoot,
            string workspaceRoot,
            string projectId,
            string pluginId,
            RecentProjectStore store,
            WorkspaceApplicationRegistry applicationRegistry,
            PluginCatalogService service,
            InMemorySecureStorage secureStorage,
            string? previousConfigDir)
        {
            Root = root;
            ConfigRoot = configRoot;
            WorkspaceRoot = workspaceRoot;
            ProjectId = projectId;
            PluginId = pluginId;
            Store = store;
            ApplicationRegistry = applicationRegistry;
            Service = service;
            SecureStorage = secureStorage;
            _previousConfigDir = previousConfigDir;
        }

        public string Root { get; }
        public string ConfigRoot { get; }
        public string WorkspaceRoot { get; }
        public string ProjectId { get; }
        public string PluginId { get; }
        public RecentProjectStore Store { get; }
        public WorkspaceApplicationRegistry ApplicationRegistry { get; }
        public PluginCatalogService Service { get; }
        public InMemorySecureStorage SecureStorage { get; }

        public static async Task<PluginCatalogFixture> CreateAsync()
        {
            var previousConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
            var root = Path.Combine(Path.GetTempPath(), "clawsharp-agenthost-plugin-tests", Guid.NewGuid().ToString("N"));
            var configRoot = Path.Combine(root, ".clawsharp");
            var workspaceRoot = Path.Combine(root, "repo");
            var pluginsRoot = Path.Combine(configRoot, "plugins");
            var pluginInstallPath = Path.Combine(pluginsRoot, "cache", "anthropic-tools", "reviewer", "1.0.0");
            var pluginId = "reviewer@anthropic-tools";

            Directory.CreateDirectory(workspaceRoot);
            Directory.CreateDirectory(Path.Combine(workspaceRoot, ".git"));
            Directory.CreateDirectory(Path.Combine(pluginInstallPath, "skills", "reviewer"));
            Directory.CreateDirectory(pluginsRoot);
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", configRoot);

            File.WriteAllText(
                Path.Combine(pluginInstallPath, "plugin.json"),
                """
                {
                  "name": "reviewer",
                  "description": "Reviewer plugin",
                  "version": "1.0.0",
                  "skills": ["skills/reviewer"],
                  "userConfig": {
                    "workspaceRoot": {
                      "type": "directory",
                      "title": "Workspace root",
                      "description": "Directory to review",
                      "required": true
                    },
                    "apiKey": {
                      "type": "string",
                      "title": "API key",
                      "description": "Token",
                      "sensitive": true
                    }
                  }
                }
                """,
                Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(pluginInstallPath, "skills", "reviewer", "SKILL.md"),
                "# Reviewer\n\nPlugin-provided skill.",
                Encoding.UTF8);
            File.WriteAllText(
                Path.Combine(pluginsRoot, "installed_plugins.json"),
                $$"""
                {
                  "version": 2,
                  "plugins": {
                    "{{pluginId}}": [
                      {
                        "scope": "user",
                        "installPath": "{{pluginInstallPath.Replace("\\", "\\\\", StringComparison.Ordinal)}}",
                        "version": "1.0.0",
                        "installedAt": "2026-04-01T09:00:00Z",
                        "lastUpdated": "2026-04-05T12:30:00Z",
                        "gitCommitSha": "abc123"
                      }
                    ]
                  }
                }
                """,
                Encoding.UTF8);

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
            var secureStorage = new InMemorySecureStorage();
            var service = new PluginCatalogService(
                applicationRegistry,
                store,
                secureStorage);

            return new PluginCatalogFixture(
                root,
                configRoot,
                workspaceRoot,
                projectId,
                pluginId,
                store,
                applicationRegistry,
                service,
                secureStorage,
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

    public sealed class InMemorySecureStorage : IMcpSecureStorage
    {
        private McpSecureStorageData? _data;

        public McpSecureStorageData? Read() => _data;

        public Task<McpSecureStorageData?> ReadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_data);

        public void Update(McpSecureStorageData data) => _data = data;

        public bool Delete()
        {
            _data = null;
            return true;
        }
    }
}
