// TS origin: ./services/mcp/client.ts, ./tools/MCPTool/MCPTool.ts, ./tools/ListMcpResourcesTool/ListMcpResourcesTool.ts, ./tools/ReadMcpResourceTool/ReadMcpResourceTool.ts
using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Tools;

namespace ClawSharp.IntegrationTests;

public sealed class McpBehaviorIntegrationTests
{
    [Fact]
    public async Task Mcp_Services_Register_Tools_Prompts_And_Resources_And_Execute_Them()
    {
        var workspaceRoot = CreateTempDirectory("clawsharp-mcp-integration");

        try
        {
            var fakeSession = new FakeMcpClientSession();
            var connector = new FakeMcpClientConnector(fakeSession);
            var lifecycleManager = new McpLifecycleManager(connector);
            var toolRegistry = new ToolRegistry(workspaceRoot, new ClawSharp.Tasks.TaskRegistry(workspaceRoot));
            var toolRegistration = new McpToolRegistrationService(lifecycleManager);
            var promptRegistry = new McpPromptCommandRegistry();
            var resourceCatalog = new McpResourceCatalog();
            var commandResourceRegistration = new McpCommandResourceRegistrationService(
                lifecycleManager,
                promptRegistry,
                resourceCatalog);
            var server = new ScopedMcpServerConfig(
                "demo-server",
                new McpStdioServerConfig("demo", [], null),
                McpConfigScope.User);
            var connections = await lifecycleManager.ConnectServersAsync(
                new Dictionary<string, ScopedMcpServerConfig>(StringComparer.Ordinal)
                {
                    ["demo-server"] = server
                });

            var registeredTools = await toolRegistration.RegisterToolsAsync(toolRegistry, connections);
            await commandResourceRegistration.RegisterForConnectionsAsync(toolRegistry, connections);

            var session = new DefaultSessionFactory(workspaceRoot).Create();
            var settings = new ClawSharpSettings();
            var registeredTool = Assert.Single(registeredTools);
            var toolResult = await toolRegistry.ExecuteAsync(
                registeredTool.Descriptor.Name,
                """{"path":"README.md"}""",
                session,
                settings);
            var listResourcesResult = await toolRegistry.ExecuteAsync(
                "ListMcpResourcesTool",
                """{}""",
                session,
                settings);
            var readResourceResult = await toolRegistry.ExecuteAsync(
                "ReadMcpResourceTool",
                """{"server":"demo-server","uri":"docs://readme"}""",
                session,
                settings);

            Assert.Equal("mcp__demo-server__inspect_file", registeredTool.Descriptor.Name);
            Assert.True(toolResult.Success, toolResult.Output);
            Assert.Equal("ok", Assert.IsType<JsonObject>(toolResult.StructuredOutput)["status"]?.GetValue<string>());
            Assert.Equal("README.md", fakeSession.ToolCalls.Single()["path"]?.GetValue<string>());

            Assert.True(listResourcesResult.Success, listResourcesResult.Output);
            Assert.Contains("docs://readme", listResourcesResult.Output, StringComparison.Ordinal);

            Assert.True(readResourceResult.Success, readResourceResult.Output);
            var resourceOutput = Assert.IsType<JsonObject>(readResourceResult.StructuredOutput);
            Assert.Equal("docs://readme", resourceOutput["contents"]?[0]?["uri"]?.GetValue<string>());
            Assert.Equal("ClawSharp MCP resource", resourceOutput["contents"]?[0]?["text"]?.GetValue<string>());

            Assert.True(promptRegistry.TryResolve("mcp__demo-server__review", out var promptHandler));
            var promptMessages = await promptHandler!.GetPromptMessagesAsync(
                "README.md",
                new CommandExecutionContext
                {
                    AppStateStore = new NullClawSharpAppStateStore(workspaceRoot),
                    Session = session,
                    SessionFactory = new DefaultSessionFactory(workspaceRoot),
                    TranscriptStore = new JsonlTranscriptStore(),
                    Settings = settings
                });
            var promptMessage = Assert.Single(promptMessages);
            Assert.Equal(MessageRole.User, promptMessage.Role);
            Assert.Contains("README.md", promptMessage.Content, StringComparison.Ordinal);
            Assert.Equal(1, connector.ConnectCalls);
        }
        finally
        {
            DeleteDirectory(workspaceRoot);
        }
    }

    private static string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class FakeMcpClientConnector : IMcpClientConnector
    {
        private readonly FakeMcpClientSession _session;
        private readonly Dictionary<string, object?> _capabilities =
            new(StringComparer.Ordinal)
            {
                ["tools"] = true,
                ["prompts"] = true,
                ["resources"] = true
            };

        public FakeMcpClientConnector(FakeMcpClientSession session)
        {
            _session = session;
        }

        public int ConnectCalls { get; private set; }

        public Task<McpServerConnection> ConnectAsync(
            string name,
            ScopedMcpServerConfig server,
            McpServerConnectionStatistics? serverStatistics = null,
            CancellationToken cancellationToken = default)
        {
            ConnectCalls++;
            return Task.FromResult<McpServerConnection>(
                new ConnectedMcpServerConnection(
                    name,
                    server,
                    _session,
                    _capabilities,
                    CleanupAsync: () => Task.CompletedTask));
        }
    }

    private sealed class FakeMcpClientSession : IMcpClientSession
    {
        public List<JsonObject> ToolCalls { get; } = [];

        public Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<McpToolDefinition>>(
                [
                    new McpToolDefinition(
                        "inspect.file",
                        "Inspect a file",
                        new JsonObject
                        {
                            ["type"] = "object"
                        },
                        new McpToolAnnotations(ReadOnlyHint: true))
                ]);
        }

        public Task<IReadOnlyList<McpPromptDefinition>> ListPromptsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<McpPromptDefinition>>(
                [
                    new McpPromptDefinition(
                        "review",
                        "Review a file",
                        [
                            new McpPromptArgumentDefinition("path", true, "Path to review")
                        ])
                ]);
        }

        public Task<McpPromptResult> GetPromptAsync(
            string promptName,
            IReadOnlyDictionary<string, string?> arguments,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new McpPromptResult(
                    [
                        ChatMessageFactory.CreateText(
                            MessageRole.User,
                            $"Review {arguments["path"]}")
                    ]));
        }

        public Task<IReadOnlyList<McpResourceDefinition>> ListResourcesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<McpResourceDefinition>>(
                [
                    new McpResourceDefinition("docs://readme", "README", "text/plain", "Workspace readme")
                ]);
        }

        public Task<McpReadResourceResult> ReadResourceAsync(
            string uri,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new McpReadResourceResult(
                    [
                        new McpResourceContent(uri, "text/plain", "ClawSharp MCP resource")
                    ]));
        }

        public Task SendNotificationAsync(
            string method,
            JsonObject? parameters = null,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<McpToolCallResult> CallToolAsync(
            string toolName,
            JsonObject arguments,
            Action<McpToolProgressNotification>? onProgress = null,
            CancellationToken cancellationToken = default)
        {
            ToolCalls.Add(arguments.DeepClone()!.AsObject());
            onProgress?.Invoke(new McpToolProgressNotification(0.5d, 1d, "halfway"));
            return Task.FromResult(
                new McpToolCallResult(
                    "tool ok",
                    StructuredContent: new JsonObject
                    {
                        ["status"] = "ok"
                    }));
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }

        public void SetElicitationRequestHandler(Func<McpElicitationRequestContext, Task<McpElicitResult>> handler)
        {
        }

        public void SetElicitationCompletionHandler(Action<string> handler)
        {
        }
    }
}
