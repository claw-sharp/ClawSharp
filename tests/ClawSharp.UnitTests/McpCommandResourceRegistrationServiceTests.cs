// TS origin: ./services/mcp/client.ts, ./tools/ListMcpResourcesTool/ListMcpResourcesTool.ts, ./tools/ReadMcpResourceTool/ReadMcpResourceTool.ts, ./types/command.ts
using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Tasks;
using ClawSharp.Tools;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public sealed class McpCommandResourceRegistrationServiceTests
{
    [Fact]
    public async Task RegisterForConnectionsAsync_RegistersPromptCommandsAndResources()
    {
        var session = new RecordingMcpClientSession(
            prompts:
            [
                new McpPromptDefinition(
                    "summarize",
                    "Summarize project state",
                    [new McpPromptArgumentDefinition("topic")])
            ],
            resources:
            [
                new McpResourceDefinition("resource://one", "One", "text/plain", "desc")
            ]);
        var promptRegistry = new McpPromptCommandRegistry();
        var resourceCatalog = new McpResourceCatalog();
        var service = new McpCommandResourceRegistrationService(
            new McpLifecycleManager(new RecordingMcpClientConnector(session)),
            promptRegistry,
            resourceCatalog);
        var tools = CreateToolRegistry();
        var connection = CreateConnectedConnection(
            "server.one",
            new McpHttpServerConfig("https://example.test", null, null, null),
            session,
            capabilities: ["prompts", "resources"]);

        await service.RegisterForConnectionsAsync(tools, [connection]);

        Assert.True(promptRegistry.TryResolve("mcp__server_one__summarize", out var command));
        Assert.NotNull(command);
        var resources = resourceCatalog.GetResources();
        var resource = Assert.Single(resources);
        Assert.Equal("server.one", resource.Server);
        Assert.True(tools.TryResolve("ListMcpResourcesTool", out _));
        Assert.True(tools.TryResolve("ReadMcpResourceTool", out _));
    }

    [Fact]
    public async Task PromptCommandHandler_GetPromptMessagesAsync_UsesLifecycleConnectionAndArgumentZipping()
    {
        var promptMessages = new[]
        {
            ChatMessageFactory.CreateText(MessageRole.User, "Prompt result")
        };
        var session = new RecordingMcpClientSession(
            prompts:
            [
                new McpPromptDefinition(
                    "summarize",
                    "Summarize",
                    [
                        new McpPromptArgumentDefinition("topic"),
                        new McpPromptArgumentDefinition("scope")
                    ])
            ],
            promptResult: new McpPromptResult(promptMessages));
        var promptRegistry = new McpPromptCommandRegistry();
        var resourceCatalog = new McpResourceCatalog();
        var service = new McpCommandResourceRegistrationService(
            new McpLifecycleManager(new RecordingMcpClientConnector(session)),
            promptRegistry,
            resourceCatalog);
        var tools = CreateToolRegistry();
        var connection = CreateConnectedConnection(
            "prompt-server",
            new McpSdkServerConfig("prompt-server"),
            session,
            capabilities: ["prompts"]);

        await service.RegisterForConnectionsAsync(tools, [connection]);
        Assert.True(promptRegistry.TryResolve("mcp__prompt-server__summarize", out var command));

        var messages = await command!.GetPromptMessagesAsync(
            "docs repo",
            new CommandExecutionContext
            {
                AppStateStore = CreateAppStateStore(),
                Session = new ConversationSession("session-1", Environment.CurrentDirectory),
                SessionFactory = new DefaultSessionFactory(Environment.CurrentDirectory),
                TranscriptStore = new JsonlTranscriptStore(),
                Settings = new ClawSharpSettings()
            });

        Assert.Single(messages);
        Assert.Equal("summarize", session.LastPromptName);
        Assert.Equal("docs", session.LastPromptArguments?["topic"]);
        Assert.Equal("repo", session.LastPromptArguments?["scope"]);
    }

    [Fact]
    public async Task ReadMcpResourceTool_ReadsTextAndPersistsBinaryBlob()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "clawsharp-mcp-resource-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var session = new RecordingMcpClientSession(
                resources:
                [
                    new McpResourceDefinition("resource://text", "Text"),
                    new McpResourceDefinition("resource://blob", "Blob", "application/pdf")
                ],
                readResourceResult: new McpReadResourceResult(
                    [
                        new McpResourceContent("resource://text", "text/plain", "hello"),
                        new McpResourceContent("resource://blob", "application/pdf", Blob: [1, 2, 3, 4])
                    ]));
            var promptRegistry = new McpPromptCommandRegistry();
            var resourceCatalog = new McpResourceCatalog();
            var service = new McpCommandResourceRegistrationService(
                new McpLifecycleManager(new RecordingMcpClientConnector(session)),
                promptRegistry,
                resourceCatalog);
            var tools = CreateToolRegistry();
            var connection = CreateConnectedConnection(
                "resource-server",
                new McpStdioServerConfig("echo", [], null),
                session,
                capabilities: ["resources"]);

            await service.RegisterForConnectionsAsync(tools, [connection]);

            var executionSession = new ConversationSession("session-1", tempRoot);
            var result = await tools.ExecuteAsync(
                "ReadMcpResourceTool",
                "{\"server\":\"resource-server\",\"uri\":\"resource://blob\"}",
                executionSession,
                new ClawSharpSettings());

            Assert.True(result.Success);
            Assert.Contains("blobSavedTo", result.Output);
            Assert.Equal("resource://blob", session.LastReadResourceUri);
            var toolResultsDir = Path.Combine(SessionStoragePaths.GetProjectDir(executionSession.ProjectDirectory), executionSession.Id, "tool-results");
            Assert.True(Directory.Exists(toolResultsDir));
            Assert.NotEmpty(Directory.EnumerateFiles(toolResultsDir));
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
    public async Task PromptCommandHandler_RetriesOnceAfterSessionExpiredError()
    {
        var promptMessages = new[]
        {
            ChatMessageFactory.CreateText(MessageRole.User, "Prompt result")
        };
        var failingSession = new ReconnectablePromptResourceSession(
            promptExceptionFactory: CreateSessionExpiredException);
        var succeedingSession = new ReconnectablePromptResourceSession(
            promptResult: new McpPromptResult(promptMessages));
        var promptRegistry = new McpPromptCommandRegistry();
        var resourceCatalog = new McpResourceCatalog();
        var service = new McpCommandResourceRegistrationService(
            new McpLifecycleManager(new SequencedPromptResourceConnector(failingSession, succeedingSession)),
            promptRegistry,
            resourceCatalog);
        var tools = CreateToolRegistry();
        var connection = CreateConnectedConnection(
            "prompt-retry",
            new McpHttpServerConfig("https://example.test", null, null, null),
            succeedingSession,
            capabilities: ["prompts"]);

        await service.RegisterForConnectionsAsync(tools, [connection]);
        Assert.True(promptRegistry.TryResolve("mcp__prompt-retry__summarize", out var command));

        var result = await command!.GetPromptMessagesAsync(
            string.Empty,
            new CommandExecutionContext
            {
                AppStateStore = CreateAppStateStore(),
                Session = new ConversationSession("session-1", Environment.CurrentDirectory),
                SessionFactory = new DefaultSessionFactory(Environment.CurrentDirectory),
                TranscriptStore = new JsonlTranscriptStore(),
                Settings = new ClawSharpSettings()
            });

        Assert.Single(result);
        Assert.Equal(1, failingSession.PromptCalls);
        Assert.Equal(1, succeedingSession.PromptCalls);
    }

    [Fact]
    public async Task ReadMcpResourceTool_RetriesOnceAfterConnectionClosedOnHttp()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "clawsharp-mcp-resource-retry-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var failingSession = new ReconnectablePromptResourceSession(
                resourceExceptionFactory: CreateConnectionClosedException);
            var succeedingSession = new ReconnectablePromptResourceSession(
                readResourceResult: new McpReadResourceResult(
                [
                    new McpResourceContent("resource://blob", "text/plain", "ok")
                ]));
            var promptRegistry = new McpPromptCommandRegistry();
            var resourceCatalog = new McpResourceCatalog();
            var service = new McpCommandResourceRegistrationService(
                new McpLifecycleManager(new SequencedPromptResourceConnector(failingSession, succeedingSession)),
                promptRegistry,
                resourceCatalog);
            var tools = CreateToolRegistry();
            var connection = CreateConnectedConnection(
                "resource-retry",
                new McpHttpServerConfig("https://example.test", null, null, null),
                succeedingSession,
                capabilities: ["resources"]);

            await service.RegisterForConnectionsAsync(tools, [connection]);

            var executionSession = new ConversationSession("session-1", tempRoot);
            var result = await tools.ExecuteAsync(
                "ReadMcpResourceTool",
                "{\"server\":\"resource-retry\",\"uri\":\"resource://blob\"}",
                executionSession,
                new ClawSharpSettings());

            Assert.True(result.Success);
            Assert.Equal(1, failingSession.ResourceCalls);
            Assert.Equal(1, succeedingSession.ResourceCalls);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static ToolRegistry CreateToolRegistry()
    {
        return new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry(), FileStateCache.CreateWithSizeLimit(FileStateCache.DefaultMaxEntries), ToolPermissionContexts.CreateEmpty());
    }

    private static IClawSharpAppStateStore CreateAppStateStore()
    {
        return new ClawSharpAppStateStore(
            ClawSharpAppState.CreateDefault(
                Environment.CurrentDirectory,
                StartupEnvironment.Capture(),
                new ClawSharpSettings(),
                [],
                [],
                [],
                [],
                [],
                []));
    }

    private static ConnectedMcpServerConnection CreateConnectedConnection(
        string name,
        McpServerConfig config,
        IMcpClientSession session,
        IReadOnlyList<string> capabilities)
    {
        return new ConnectedMcpServerConnection(
            name,
            new ScopedMcpServerConfig(name, config, McpConfigScope.User),
            session,
            capabilities.ToDictionary(key => key, _ => (object?)new Dictionary<string, object?>(), StringComparer.Ordinal),
            CleanupAsync: () => Task.CompletedTask);
    }

    private sealed class RecordingMcpClientConnector : IMcpClientConnector
    {
        private readonly IMcpClientSession _session;

        public RecordingMcpClientConnector(IMcpClientSession session)
        {
            _session = session;
        }

        public Task<McpServerConnection> ConnectAsync(
            string name,
            ScopedMcpServerConfig server,
            McpServerConnectionStatistics? serverStatistics = null,
            CancellationToken cancellationToken = default)
        {
            var capabilities = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["prompts"] = new Dictionary<string, object?>(),
                ["resources"] = new Dictionary<string, object?>()
            };
            return Task.FromResult<McpServerConnection>(
                new ConnectedMcpServerConnection(
                    name,
                    server,
                    _session,
                    capabilities,
                    CleanupAsync: () => Task.CompletedTask));
        }
    }

    private sealed class SequencedPromptResourceConnector : IMcpClientConnector
    {
        private readonly IMcpClientSession[] _sessions;
        private int _index = -1;

        public SequencedPromptResourceConnector(params IMcpClientSession[] sessions)
        {
            _sessions = sessions;
        }

        public Task<McpServerConnection> ConnectAsync(
            string name,
            ScopedMcpServerConfig server,
            McpServerConnectionStatistics? serverStatistics = null,
            CancellationToken cancellationToken = default)
        {
            var nextIndex = Math.Min(Interlocked.Increment(ref _index), _sessions.Length - 1);
            var capabilities = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["prompts"] = new Dictionary<string, object?>(),
                ["resources"] = new Dictionary<string, object?>()
            };
            return Task.FromResult<McpServerConnection>(
                new ConnectedMcpServerConnection(
                    name,
                    server,
                    _sessions[nextIndex],
                    capabilities,
                    CleanupAsync: () => Task.CompletedTask));
        }
    }

    private sealed class RecordingMcpClientSession : IMcpClientSession
    {
        private readonly IReadOnlyList<McpPromptDefinition> _prompts;
        private readonly IReadOnlyList<McpResourceDefinition> _resources;
        private readonly McpPromptResult _promptResult;
        private readonly McpReadResourceResult _readResourceResult;

        public RecordingMcpClientSession(
            IReadOnlyList<McpPromptDefinition>? prompts = null,
            IReadOnlyList<McpResourceDefinition>? resources = null,
            McpPromptResult? promptResult = null,
            McpReadResourceResult? readResourceResult = null)
        {
            _prompts = prompts ?? [];
            _resources = resources ?? [];
            _promptResult = promptResult ?? new McpPromptResult([]);
            _readResourceResult = readResourceResult ?? new McpReadResourceResult([]);
        }

        public string? LastPromptName { get; private set; }
        public IReadOnlyDictionary<string, string?>? LastPromptArguments { get; private set; }
        public string? LastReadResourceUri { get; private set; }

        public Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<McpToolDefinition>>([]);
        }

        public Task<IReadOnlyList<McpPromptDefinition>> ListPromptsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_prompts);
        }

        public Task<McpPromptResult> GetPromptAsync(
            string promptName,
            IReadOnlyDictionary<string, string?> arguments,
            CancellationToken cancellationToken = default)
        {
            LastPromptName = promptName;
            LastPromptArguments = new Dictionary<string, string?>(arguments, StringComparer.Ordinal);
            return Task.FromResult(_promptResult);
        }

        public Task<IReadOnlyList<McpResourceDefinition>> ListResourcesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_resources);
        }

        public Task<McpReadResourceResult> ReadResourceAsync(
            string uri,
            CancellationToken cancellationToken = default)
        {
            LastReadResourceUri = uri;
            return Task.FromResult(_readResourceResult);
        }

        public Task<McpToolCallResult> CallToolAsync(
            string toolName,
            JsonObject arguments,
            Action<McpToolProgressNotification>? onProgress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new McpToolCallResult(string.Empty));
        }

        public Task SendNotificationAsync(
            string method,
            JsonObject? parameters = null,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
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

    private sealed class ReconnectablePromptResourceSession : IMcpClientSession
    {
        private readonly Func<Exception>? _promptExceptionFactory;
        private readonly Func<Exception>? _resourceExceptionFactory;
        private readonly McpPromptResult _promptResult;
        private readonly McpReadResourceResult _readResourceResult;

        public ReconnectablePromptResourceSession(
            Func<Exception>? promptExceptionFactory = null,
            Func<Exception>? resourceExceptionFactory = null,
            McpPromptResult? promptResult = null,
            McpReadResourceResult? readResourceResult = null)
        {
            _promptExceptionFactory = promptExceptionFactory;
            _resourceExceptionFactory = resourceExceptionFactory;
            _promptResult = promptResult ?? new McpPromptResult(
            [
                ChatMessageFactory.CreateText(MessageRole.User, "Prompt result")
            ]);
            _readResourceResult = readResourceResult ?? new McpReadResourceResult(
            [
                new McpResourceContent("resource://blob", "text/plain", "ok")
            ]);
        }

        public int PromptCalls { get; private set; }
        public int ResourceCalls { get; private set; }

        public Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<McpToolDefinition>>([]);

        public Task<IReadOnlyList<McpPromptDefinition>> ListPromptsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<McpPromptDefinition>>(
            [
                new McpPromptDefinition("summarize", "Summarize")
            ]);

        public Task<McpPromptResult> GetPromptAsync(
            string promptName,
            IReadOnlyDictionary<string, string?> arguments,
            CancellationToken cancellationToken = default)
        {
            PromptCalls++;
            if (_promptExceptionFactory is not null && PromptCalls == 1)
            {
                throw _promptExceptionFactory();
            }

            return Task.FromResult(_promptResult);
        }

        public Task<IReadOnlyList<McpResourceDefinition>> ListResourcesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<McpResourceDefinition>>(
            [
                new McpResourceDefinition("resource://blob", "Blob", "text/plain")
            ]);

        public Task<McpReadResourceResult> ReadResourceAsync(
            string uri,
            CancellationToken cancellationToken = default)
        {
            ResourceCalls++;
            if (_resourceExceptionFactory is not null && ResourceCalls == 1)
            {
                throw _resourceExceptionFactory();
            }

            return Task.FromResult(_readResourceResult);
        }

        public Task<McpToolCallResult> CallToolAsync(
            string toolName,
            JsonObject arguments,
            Action<McpToolProgressNotification>? onProgress = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new McpToolCallResult(string.Empty));

        public Task SendNotificationAsync(
            string method,
            JsonObject? parameters = null,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void SetElicitationRequestHandler(Func<McpElicitationRequestContext, Task<McpElicitResult>> handler)
        {
        }

        public void SetElicitationCompletionHandler(Action<string> handler)
        {
        }
    }

    private static Exception CreateSessionExpiredException()
    {
        var exception = new InvalidOperationException("{\"error\":{\"code\":-32001,\"message\":\"Session not found\"}}");
        exception.Data["code"] = 404;
        return exception;
    }

    private static Exception CreateConnectionClosedException()
    {
        var exception = new InvalidOperationException("Connection closed");
        exception.Data["code"] = -32000;
        return exception;
    }
}
