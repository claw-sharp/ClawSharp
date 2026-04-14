using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Tasks;
using ClawSharp.Tools;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public sealed class McpToolRegistrationServiceTests
{
    [Fact]
    public async Task FetchToolsForConnectionAsync_MapsTsNamingMetadataAndReadOnlyHints()
    {
        var session = new RecordingMcpClientSession(
            [
                new McpToolDefinition(
                    "search docs",
                    "Remote MCP tool",
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = true
                    },
                    new McpToolAnnotations(ReadOnlyHint: true, Title: "Search docs"),
                    new JsonObject
                    {
                        ["anthropic/searchHint"] = "  semantic   docs\n lookup  ",
                        ["anthropic/alwaysLoad"] = true
                    })
            ],
            new McpToolCallResult("unused"));
        var service = new McpToolRegistrationService(new McpLifecycleManager(new RecordingMcpClientConnector(session)));
        var connection = CreateConnectedConnection(
            "claude.ai browser",
            new McpHttpServerConfig("https://example.test", null, null, null),
            session);

        var tools = await service.FetchToolsForConnectionAsync(connection);

        var tool = Assert.Single(tools);
        Assert.Equal("mcp__claude_ai_browser__search_docs", tool.Descriptor.Name);
        Assert.Equal("semantic docs lookup", tool.Descriptor.SearchHint);
        Assert.True(tool.Descriptor.AlwaysLoad);
        Assert.NotNull(tool.Descriptor.InputSchema);
        Assert.True(tool.IsReadOnly("{}"));
        Assert.True(tool.IsConcurrencySafe("{}"));
    }

    [Fact]
    public async Task RegisterToolsAsync_SdkNoPrefixMode_ReplacesExistingRegistryEntry()
    {
        var originalValue = Environment.GetEnvironmentVariable("CLAUDE_AGENT_SDK_MCP_NO_PREFIX");
        Environment.SetEnvironmentVariable("CLAUDE_AGENT_SDK_MCP_NO_PREFIX", "true");

        try
        {
            var session = new RecordingMcpClientSession(
                [
                    new McpToolDefinition(
                        "Read",
                        "SDK MCP replacement",
                        new JsonObject
                        {
                            ["type"] = "object",
                            ["additionalProperties"] = true
                        })
                ],
                new McpToolCallResult("unused"));
            var connector = new RecordingMcpClientConnector(session);
            var service = new McpToolRegistrationService(new McpLifecycleManager(connector));
            var registry = CreateToolRegistry();
            var connection = CreateConnectedConnection(
                "sdk-server",
                new McpSdkServerConfig("sdk-server"),
                session);

            await service.RegisterToolsAsync(registry, [connection]);

            Assert.True(registry.TryResolve("Read", out var tool));
            Assert.NotNull(tool);
            Assert.Equal("SDK MCP replacement", tool!.Descriptor.Description);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_AGENT_SDK_MCP_NO_PREFIX", originalValue);
        }
    }

    [Fact]
    public async Task RegisteredTool_ExecutesThroughLifecycleManager_AndReportsMcpProgress()
    {
        var session = new RecordingMcpClientSession(
            [
                new McpToolDefinition(
                    "remote_search",
                    "Searches a remote server",
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = true
                    })
            ],
            new McpToolCallResult(
                "search complete",
                StructuredContent: new JsonObject
                {
                    ["matches"] = 3
                }),
            new McpToolProgressNotification(1, 3, "step 1"));
        var connector = new RecordingMcpClientConnector(session);
        var lifecycleManager = new McpLifecycleManager(connector);
        var service = new McpToolRegistrationService(lifecycleManager);
        var registry = CreateToolRegistry();
        var connection = CreateConnectedConnection(
            "search-server",
            new McpStdioServerConfig("echo", [], null),
            session);

        await service.RegisterToolsAsync(registry, [connection]);

        var progress = new List<ToolProgressUpdate>();
        var result = await registry.ExecuteAsync(
            "mcp__search-server__remote_search",
            "{\"query\":\"docs\"}",
            new ConversationSession("session-1", Environment.CurrentDirectory),
            new ClawSharpSettings(),
            progress.Add);

        Assert.True(result.Success);
        Assert.Equal("search complete", result.Output);
        Assert.Equal("remote_search", session.LastCalledToolName);
        Assert.Equal("docs", session.LastArguments?["query"]?.GetValue<string>());
        Assert.Collection(
            progress.Select(update => update.Data["status"]?.GetValue<string>()).Where(status => status is not null)!,
            status => Assert.Equal("started", status),
            status => Assert.Equal("progress", status),
            status => Assert.Equal("completed", status));
        Assert.NotNull(result.StructuredOutput);
    }

    [Fact]
    public async Task RegisteredTool_Maps_McpError_Result_To_Failed_ToolExecutionResult()
    {
        var session = new RecordingMcpClientSession(
            [
                new McpToolDefinition(
                    "save_project",
                    "Creates a project",
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = true
                    })
            ],
            new McpToolCallResult(
                """Variable "$input" got invalid value "" at "input.startDate"; Expected type "TimelessDate".""",
                IsError: true));
        var connector = new RecordingMcpClientConnector(session);
        var lifecycleManager = new McpLifecycleManager(connector);
        var service = new McpToolRegistrationService(lifecycleManager);
        var registry = CreateToolRegistry();
        var connection = CreateConnectedConnection(
            "linear",
            new McpHttpServerConfig("https://example.test", null, null, null),
            session);

        await service.RegisterToolsAsync(registry, [connection]);

        var progress = new List<ToolProgressUpdate>();
        var result = await registry.ExecuteAsync(
            "mcp__linear__save_project",
            "{\"name\":\"HR\"}",
            new ConversationSession("session-1", Environment.CurrentDirectory),
            new ClawSharpSettings(),
            progress.Add);

        Assert.False(result.Success);
        Assert.Contains("input.startDate", result.Output, StringComparison.Ordinal);
        Assert.Collection(
            progress.Select(update => update.Data["status"]?.GetValue<string>()).Where(status => status is not null)!,
            status => Assert.Equal("started", status),
            status => Assert.Equal("failed", status));
    }

    [Fact]
    public async Task RegisteredTool_RetriesOnceAfterSessionExpiredError()
    {
        var failingSession = new ReconnectableMcpClientSession(
            exceptionFactory: CreateSessionExpiredException,
            callResult: new McpToolCallResult("retried"));
        var succeedingSession = new ReconnectableMcpClientSession(
            callResult: new McpToolCallResult("retried"));
        var connector = new SequencedMcpClientConnector(failingSession, succeedingSession);
        var service = new McpToolRegistrationService(new McpLifecycleManager(connector));
        var registry = CreateToolRegistry();
        var connection = CreateConnectedConnection(
            "retry-server",
            new McpHttpServerConfig("https://example.test", null, null, null),
            succeedingSession);

        await service.RegisterToolsAsync(registry, [connection]);

        var result = await registry.ExecuteAsync(
            "mcp__retry-server__retry_tool",
            "{\"query\":\"docs\"}",
            new ConversationSession("session-1", Environment.CurrentDirectory),
            new ClawSharpSettings());

        Assert.True(result.Success);
        Assert.Equal("retried", result.Output);
        Assert.Equal(2, connector.ConnectCalls);
        Assert.Equal(1, failingSession.CallAttempts);
        Assert.Equal(1, succeedingSession.CallAttempts);
    }

    private static ToolRegistry CreateToolRegistry()
    {
        return new ToolRegistry(
            Environment.CurrentDirectory,
            new TaskRegistry(),
            FileStateCache.CreateWithSizeLimit(FileStateCache.DefaultMaxEntries),
            ToolPermissionContexts.CreateEmpty());
    }

    private static ConnectedMcpServerConnection CreateConnectedConnection(
        string name,
        McpServerConfig serverConfig,
        IMcpClientSession session)
    {
        var scoped = new ScopedMcpServerConfig(name, serverConfig, McpConfigScope.User);
        return new ConnectedMcpServerConnection(
            name,
            scoped,
            session,
            new Dictionary<string, object?>
            {
                ["tools"] = new Dictionary<string, object?>()
            },
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
            return Task.FromResult<McpServerConnection>(
                new ConnectedMcpServerConnection(
                    name,
                    server,
                    _session,
                    new Dictionary<string, object?>
                    {
                        ["tools"] = new Dictionary<string, object?>()
                    },
                    CleanupAsync: () => Task.CompletedTask));
        }
    }

    private sealed class SequencedMcpClientConnector : IMcpClientConnector
    {
        private readonly IMcpClientSession[] _sessions;
        private int _index = -1;

        public SequencedMcpClientConnector(params IMcpClientSession[] sessions)
        {
            _sessions = sessions;
        }

        public int ConnectCalls { get; private set; }

        public Task<McpServerConnection> ConnectAsync(
            string name,
            ScopedMcpServerConfig server,
            McpServerConnectionStatistics? serverStatistics = null,
            CancellationToken cancellationToken = default)
        {
            ConnectCalls++;
            var nextIndex = Math.Min(Interlocked.Increment(ref _index), _sessions.Length - 1);
            return Task.FromResult<McpServerConnection>(
                new ConnectedMcpServerConnection(
                    name,
                    server,
                    _sessions[nextIndex],
                    new Dictionary<string, object?>
                    {
                        ["tools"] = new Dictionary<string, object?>()
                    },
                    CleanupAsync: () => Task.CompletedTask));
        }
    }

    private sealed class RecordingMcpClientSession : IMcpClientSession
    {
        private readonly IReadOnlyList<McpToolDefinition> _tools;
        private readonly McpToolCallResult _callResult;
        private readonly McpToolProgressNotification? _progress;

        public RecordingMcpClientSession(
            IReadOnlyList<McpToolDefinition> tools,
            McpToolCallResult callResult,
            McpToolProgressNotification? progress = null)
        {
            _tools = tools;
            _callResult = callResult;
            _progress = progress;
        }

        public string? LastCalledToolName { get; private set; }

        public JsonObject? LastArguments { get; private set; }

        public Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_tools);
        }

        public Task<IReadOnlyList<McpPromptDefinition>> ListPromptsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<McpPromptDefinition>>([]);
        }

        public Task<McpPromptResult> GetPromptAsync(
            string promptName,
            IReadOnlyDictionary<string, string?> arguments,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new McpPromptResult([]));
        }

        public Task<IReadOnlyList<McpResourceDefinition>> ListResourcesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<McpResourceDefinition>>([]);
        }

        public Task<McpReadResourceResult> ReadResourceAsync(
            string uri,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new McpReadResourceResult([]));
        }

        public Task<McpToolCallResult> CallToolAsync(
            string toolName,
            JsonObject arguments,
            Action<McpToolProgressNotification>? onProgress = null,
            CancellationToken cancellationToken = default)
        {
            LastCalledToolName = toolName;
            LastArguments = arguments.DeepClone().AsObject();

            if (_progress is not null)
            {
                onProgress?.Invoke(_progress);
            }

            return Task.FromResult(_callResult);
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

    private sealed class ReconnectableMcpClientSession : IMcpClientSession
    {
        private readonly Func<Exception>? _exceptionFactory;
        private readonly McpToolCallResult _callResult;

        public ReconnectableMcpClientSession(
            Func<Exception>? exceptionFactory = null,
            McpToolCallResult? callResult = null)
        {
            _exceptionFactory = exceptionFactory;
            _callResult = callResult ?? new McpToolCallResult("ok");
        }

        public int CallAttempts { get; private set; }

        public Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<McpToolDefinition>>(
            [
                new McpToolDefinition(
                    "retry_tool",
                    "Retry tool",
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = true
                    })
            ]);
        }

        public Task<IReadOnlyList<McpPromptDefinition>> ListPromptsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<McpPromptDefinition>>([]);

        public Task<McpPromptResult> GetPromptAsync(
            string promptName,
            IReadOnlyDictionary<string, string?> arguments,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new McpPromptResult([]));

        public Task<IReadOnlyList<McpResourceDefinition>> ListResourcesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<McpResourceDefinition>>([]);

        public Task<McpReadResourceResult> ReadResourceAsync(string uri, CancellationToken cancellationToken = default)
            => Task.FromResult(new McpReadResourceResult([]));

        public Task<McpToolCallResult> CallToolAsync(
            string toolName,
            JsonObject arguments,
            Action<McpToolProgressNotification>? onProgress = null,
            CancellationToken cancellationToken = default)
        {
            CallAttempts++;
            if (_exceptionFactory is not null && CallAttempts == 1)
            {
                throw _exceptionFactory();
            }

            return Task.FromResult(_callResult);
        }

        public Task SendNotificationAsync(
            string method,
            JsonObject? parameters = null,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

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
}
