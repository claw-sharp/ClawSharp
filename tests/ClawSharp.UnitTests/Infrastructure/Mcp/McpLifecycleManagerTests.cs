using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public sealed class McpLifecycleManagerTests
{
    [Fact]
    public async Task ConnectToServerAsync_ReusesCachedConnectionForSameServerKey()
    {
        var connector = new RecordingMcpClientConnector();
        var manager = new McpLifecycleManager(connector);
        var server = CreateScopedServer("server", new McpStdioServerConfig("echo", [], null));

        var first = await manager.ConnectToServerAsync("server", server);
        var second = await manager.ConnectToServerAsync("server", server);

        Assert.Same(first, second);
        Assert.Equal(1, connector.ConnectCalls);
    }

    [Fact]
    public async Task ClearServerCacheAsync_CallsCleanupForConnectedClient()
    {
        var connector = new RecordingMcpClientConnector();
        var manager = new McpLifecycleManager(connector);
        var server = CreateScopedServer("server", new McpStdioServerConfig("echo", [], null));

        await manager.ConnectToServerAsync("server", server);
        await manager.ClearServerCacheAsync("server", server);

        Assert.Equal(1, connector.CleanupCalls);
    }

    [Fact]
    public async Task ConnectServersAsync_EmitsDisabledEntriesWithoutConnectingThem()
    {
        var connector = new RecordingMcpClientConnector();
        var manager = new McpLifecycleManager(connector);
        var servers = new Dictionary<string, ScopedMcpServerConfig>(StringComparer.Ordinal)
        {
            ["enabled"] = CreateScopedServer("enabled", new McpStdioServerConfig("echo", [], null)),
            ["disabled"] = CreateScopedServer("disabled", new McpHttpServerConfig("https://example.test", null, null, null))
        };

        var attempts = new List<McpServerConnection>();
        var results = await manager.ConnectServersAsync(
            servers,
            isDisabled: name => string.Equals(name, "disabled", StringComparison.Ordinal),
            onConnectionAttempt: attempts.Add);

        Assert.Equal(1, connector.ConnectCalls);
        Assert.Contains(results, connection => connection is DisabledMcpServerConnection disabled && disabled.Name == "disabled");
        Assert.Contains(attempts, connection => connection is DisabledMcpServerConnection disabled && disabled.Name == "disabled");
    }

    [Fact]
    public async Task ConnectServersAsync_UsesConfiguredLocalAndRemoteBatchSizes()
    {
        var originalLocal = Environment.GetEnvironmentVariable("MCP_SERVER_CONNECTION_BATCH_SIZE");
        var originalRemote = Environment.GetEnvironmentVariable("MCP_REMOTE_SERVER_CONNECTION_BATCH_SIZE");
        Environment.SetEnvironmentVariable("MCP_SERVER_CONNECTION_BATCH_SIZE", "1");
        Environment.SetEnvironmentVariable("MCP_REMOTE_SERVER_CONNECTION_BATCH_SIZE", "2");

        try
        {
            var connector = new DelayedMcpClientConnector();
            var manager = new McpLifecycleManager(connector);
            var servers = new Dictionary<string, ScopedMcpServerConfig>(StringComparer.Ordinal)
            {
                ["local-1"] = CreateScopedServer("local-1", new McpStdioServerConfig("echo", [], null)),
                ["local-2"] = CreateScopedServer("local-2", new McpSdkServerConfig("sdk")),
                ["local-3"] = CreateScopedServer("local-3", new McpStdioServerConfig("echo", [], null)),
                ["remote-1"] = CreateScopedServer("remote-1", new McpHttpServerConfig("https://one.test", null, null, null)),
                ["remote-2"] = CreateScopedServer("remote-2", new McpSseServerConfig("https://two.test", null, null, null)),
                ["remote-3"] = CreateScopedServer("remote-3", new McpHttpServerConfig("https://three.test", null, null, null)),
                ["remote-4"] = CreateScopedServer("remote-4", new McpWebSocketServerConfig("wss://four.test", null, null))
            };

            await manager.ConnectServersAsync(servers);

            Assert.Equal(1, connector.MaxLocalConcurrency);
            Assert.Equal(2, connector.MaxRemoteConcurrency);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MCP_SERVER_CONNECTION_BATCH_SIZE", originalLocal);
            Environment.SetEnvironmentVariable("MCP_REMOTE_SERVER_CONNECTION_BATCH_SIZE", originalRemote);
        }
    }

    [Fact]
    public async Task ConnectServersAsync_ReturnsNeedsAuthWhenServerIsCached()
    {
        using var environment = new ClaudeConfigDirectoryScope();
        var connector = new RecordingMcpClientConnector();
        var cache = new McpNeedsAuthCache(() => new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero));
        await cache.SetEntryAsync("remote");
        var manager = new McpLifecycleManager(connector, needsAuthCache: cache);
        var servers = new Dictionary<string, ScopedMcpServerConfig>(StringComparer.Ordinal)
        {
            ["remote"] = CreateScopedServer("remote", new McpHttpServerConfig("https://example.test", null, null, null))
        };

        var results = await manager.ConnectServersAsync(servers);

        var connection = Assert.Single(results);
        Assert.IsType<NeedsAuthMcpServerConnection>(connection);
        Assert.Equal(0, connector.ConnectCalls);
    }

    [Fact]
    public async Task ConnectServersAsync_ReturnsNeedsAuthWhenDiscoveryExistsWithoutToken()
    {
        var connector = new RecordingMcpClientConnector();
        var storage = new InMemoryMcpSecureStorage();
        var authState = new McpAuthStateService(storage);
        var serverConfig = new McpSseServerConfig("https://example.test/sse", null, null, null);
        authState.SaveOAuthEntry(
            "remote",
            serverConfig,
            new McpOAuthEntry(
                "remote",
                "https://example.test/sse",
                string.Empty,
                0,
                RefreshToken: string.Empty,
                DiscoveryState: new McpOAuthDiscoveryState("https://auth.example.test")));
        var manager = new McpLifecycleManager(connector, authState);
        var servers = new Dictionary<string, ScopedMcpServerConfig>(StringComparer.Ordinal)
        {
            ["remote"] = CreateScopedServer("remote", serverConfig)
        };

        var results = await manager.ConnectServersAsync(servers);

        var connection = Assert.Single(results);
        Assert.IsType<NeedsAuthMcpServerConnection>(connection);
        Assert.Equal(0, connector.ConnectCalls);
    }

    private static ScopedMcpServerConfig CreateScopedServer(string name, McpServerConfig config)
    {
        return new ScopedMcpServerConfig(name, config, McpConfigScope.User);
    }

    private sealed class FakeMcpClientSession : IMcpClientSession
    {
        public Task<IReadOnlyList<McpToolDefinition>> ListToolsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<McpToolDefinition>>([]);
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

    private sealed class RecordingMcpClientConnector : IMcpClientConnector
    {
        private int _connectCalls;
        private int _cleanupCalls;

        public int ConnectCalls => _connectCalls;

        public int CleanupCalls => _cleanupCalls;

        public Task<McpServerConnection> ConnectAsync(
            string name,
            ScopedMcpServerConfig server,
            McpServerConnectionStatistics? serverStatistics = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _connectCalls);
            return Task.FromResult<McpServerConnection>(
                new ConnectedMcpServerConnection(
                    name,
                    server,
                    new FakeMcpClientSession(),
                    new Dictionary<string, object?>(),
                    () =>
                    {
                        Interlocked.Increment(ref _cleanupCalls);
                        return Task.CompletedTask;
                    }));
        }
    }

    private sealed class DelayedMcpClientConnector : IMcpClientConnector
    {
        private int _currentLocalConcurrency;
        private int _currentRemoteConcurrency;

        public int MaxLocalConcurrency { get; private set; }

        public int MaxRemoteConcurrency { get; private set; }

        public async Task<McpServerConnection> ConnectAsync(
            string name,
            ScopedMcpServerConfig server,
            McpServerConnectionStatistics? serverStatistics = null,
            CancellationToken cancellationToken = default)
        {
            var isLocal = McpLifecycleManager.IsLocalMcpServer(server);
            if (isLocal)
            {
                var current = Interlocked.Increment(ref _currentLocalConcurrency);
                MaxLocalConcurrency = Math.Max(MaxLocalConcurrency, current);
            }
            else
            {
                var current = Interlocked.Increment(ref _currentRemoteConcurrency);
                MaxRemoteConcurrency = Math.Max(MaxRemoteConcurrency, current);
            }

            try
            {
                await Task.Delay(40, cancellationToken);
                return new FailedMcpServerConnection(name, server);
            }
            finally
            {
                if (isLocal)
                {
                    Interlocked.Decrement(ref _currentLocalConcurrency);
                }
                else
                {
                    Interlocked.Decrement(ref _currentRemoteConcurrency);
                }
            }
        }
    }

    private sealed class InMemoryMcpSecureStorage : IMcpSecureStorage
    {
        private McpSecureStorageData? _data;

        public McpSecureStorageData? Read() => _data;

        public Task<McpSecureStorageData?> ReadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_data);
        }

        public void Update(McpSecureStorageData data)
        {
            _data = data;
        }

        public bool Delete()
        {
            _data = null;
            return true;
        }
    }

    private sealed class ClaudeConfigDirectoryScope : IDisposable
    {
        private readonly string? _original;

        public ClaudeConfigDirectoryScope()
        {
            _original = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clawsharp-mcp-lifecycle-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", _original);
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
