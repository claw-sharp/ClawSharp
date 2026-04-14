using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Tasks;
using ClawSharp.Tools;
using ClawSharp.Tools.Mcp;

namespace ClawSharp.UnitTests;

public sealed class McpAuthToolTests
{
    [Fact]
    public async Task ExecuteAsync_UsesRuntimeCoordinatorForHttpServers()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-mcp-auth", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var config = new ScopedMcpServerConfig(
                "linear",
                new McpHttpServerConfig("https://mcp.linear.app/mcp", null, null, null),
                McpConfigScope.Project);
            var tool = new McpAuthTool("linear", config);
            var coordinator = new RecordingCoordinator(new McpToolAuthenticationResult(true, "Authenticated linear."));
            var toolRegistry = new ToolRegistry(workspaceRoot, new TaskRegistry(workspaceRoot));
            var progressUpdates = new List<ToolProgressUpdate>();
            var context = CreateContext(
                workspaceRoot,
                toolRegistry,
                coordinator,
                progressUpdates.Add);

            var result = await tool.ExecuteAsync(context);

            Assert.True(result.Success);
            Assert.Equal("Authenticated linear.", result.Output);
            Assert.Equal("linear", coordinator.ServerName);
            Assert.Equal(config, coordinator.Config);
            Assert.Equal(tool.Descriptor.Name, coordinator.AuthenticateToolName);
            Assert.Same(toolRegistry, coordinator.ToolRegistry);

            Assert.Collection(
                progressUpdates,
                update =>
                {
                    Assert.Equal(tool.Descriptor.Name, update.ToolUseId);
                    Assert.Equal("starting_oauth", update.Data["status"]?.GetValue<string>());
                    Assert.Equal("linear", update.Data["server"]?.GetValue<string>());
                },
                update =>
                {
                    Assert.Equal(tool.Descriptor.Name, update.ToolUseId);
                    Assert.Equal("authenticated", update.Data["status"]?.GetValue<string>());
                    Assert.Equal("linear", update.Data["server"]?.GetValue<string>());
                });
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsManualMessageForUnsupportedTransport()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-mcp-auth", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var config = new ScopedMcpServerConfig(
                "linear",
                new McpStdioServerConfig("npx", [], null),
                McpConfigScope.User);
            var tool = new McpAuthTool("linear", config);
            var coordinator = new RecordingCoordinator(new McpToolAuthenticationResult(true, "should not be used"));
            var toolRegistry = new ToolRegistry(workspaceRoot, new TaskRegistry(workspaceRoot));
            var context = CreateContext(workspaceRoot, toolRegistry, coordinator, _ => { });

            var result = await tool.ExecuteAsync(context);

            Assert.True(result.Success);
            Assert.Contains("does not support OAuth from this tool", result.Output, StringComparison.Ordinal);
            Assert.Null(coordinator.ServerName);
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    private static ToolExecutionContext CreateContext(
        string workspaceRoot,
        ToolRegistry toolRegistry,
        IMcpToolRuntimeCoordinator coordinator,
        Action<ToolProgressUpdate> onProgress)
    {
        return new ToolExecutionContext(
            Arguments: "{}",
            WorkspaceRoot: workspaceRoot,
            Session: new ConversationSession("session-1", workspaceRoot),
            AppStateStore: new NullClawSharpAppStateStore(workspaceRoot),
            Tasks: new TaskRegistry(workspaceRoot),
            TaskAppState: new TaskRegistry(workspaceRoot),
            Settings: new ClawSharpSettings(),
            ReadFileState: FileStateCache.CreateWithSizeLimit(16),
            ToolPermissionContext: ToolPermissionContexts.CreateEmpty(),
            AgentDefinitions: [],
            FileUpdateNotifier: new NullFileUpdateNotifier(),
            PermissionPrompter: new NullPermissionPrompter(),
            WorktreeService: new ClawSharp.Core.Worktree.NullWorktreeService(),
            McpResources: new ClawSharp.Core.McpResourceCatalog(),
            McpLifecycle: null,
            McpToolRuntimeCoordinator: coordinator,
            SettingsStore: null,
            OnProgress: onProgress,
            ToolRegistry: toolRegistry);
    }

    private sealed class RecordingCoordinator : IMcpToolRuntimeCoordinator
    {
        private readonly McpToolAuthenticationResult _result;

        public RecordingCoordinator(McpToolAuthenticationResult result)
        {
            _result = result;
        }

        public string? ServerName { get; private set; }
        public ScopedMcpServerConfig? Config { get; private set; }
        public ToolRegistry? ToolRegistry { get; private set; }
        public string? AuthenticateToolName { get; private set; }

        public Task<McpToolAuthenticationResult> AuthenticateAsync(
            string serverName,
            ScopedMcpServerConfig config,
            ToolRegistry toolRegistry,
            string authenticateToolName,
            CancellationToken cancellationToken = default)
        {
            ServerName = serverName;
            Config = config;
            ToolRegistry = toolRegistry;
            AuthenticateToolName = authenticateToolName;
            return Task.FromResult(_result);
        }
    }
}
