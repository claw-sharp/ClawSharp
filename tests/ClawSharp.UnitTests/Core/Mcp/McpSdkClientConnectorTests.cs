using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public sealed class McpSdkClientConnectorTests
{
    [Fact]
    public async Task ConnectAsync_ReturnsConnectedConnectionAndRegistersElicitationHandlers()
    {
        var storage = new InMemoryMcpSecureStorage();
        var authState = new McpAuthStateService(storage);
        var transportFactory = new McpSdkHttpTransportFactory(storage, authState);
        var eventSink = new InMemoryEventSink();
        var elicitationService = new McpElicitationService(eventSink);
        var session = new RecordingMcpClientSession();
        var connector = CreateConnector(
            transportFactory,
            null,
            elicitationService,
            (_, _) => Task.FromResult<IMcpClientSession>(session));
        var server = CreateServer("server", new McpHttpServerConfig("https://example.test/mcp", null, null, null));

        var connection = await connector.ConnectAsync("server", server);

        var connected = Assert.IsType<ConnectedMcpServerConnection>(connection);
        Assert.Same(session, connected.Client);
        Assert.True(session.RequestHandlerRegistered);
        Assert.True(session.CompletionHandlerRegistered);
        await connected.CleanupAsync();
        Assert.True(session.DisposeCalled);
    }

    [Fact]
    public async Task ConnectAsync_ReturnsNeedsAuthAndCachesServerOnAuthFailure()
    {
        using var environment = new ClaudeConfigDirectoryScope();
        var storage = new InMemoryMcpSecureStorage();
        var authState = new McpAuthStateService(storage);
        var transportFactory = new McpSdkHttpTransportFactory(storage, authState);
        var cache = new McpNeedsAuthCache(() => new DateTimeOffset(2026, 4, 2, 12, 0, 0, TimeSpan.Zero));
        var connector = CreateConnector(
            transportFactory,
            cache,
            null,
            (_, _) => throw new HttpRequestException("forbidden", null, HttpStatusCode.Forbidden));
        var server = CreateServer("server", new McpSseServerConfig("https://example.test/sse", null, null, null));

        var connection = await connector.ConnectAsync("server", server);

        Assert.IsType<NeedsAuthMcpServerConnection>(connection);
        Assert.True(await cache.IsCachedAsync("server"));
    }

    [Fact]
    public async Task ConnectAsync_ReturnsFailureForUnsupportedTransportType()
    {
        var storage = new InMemoryMcpSecureStorage();
        var authState = new McpAuthStateService(storage);
        var transportFactory = new McpSdkHttpTransportFactory(storage, authState);
        var connector = new SdkMcpClientConnector(transportFactory);
        var server = CreateServer("server", new McpStdioServerConfig("echo", [], null));

        var connection = await connector.ConnectAsync("server", server);

        var failed = Assert.IsType<FailedMcpServerConnection>(connection);
        Assert.Contains("not implemented yet", failed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectAsync_UsesSseIdeTransportOptions()
    {
        var storage = new InMemoryMcpSecureStorage();
        var authState = new McpAuthStateService(storage);
        var transportFactory = new McpSdkHttpTransportFactory(storage, authState);
        var session = new RecordingMcpClientSession();
        HttpClientTransportOptions? capturedOptions = null;
        var connector = CreateConnector(
            transportFactory,
            null,
            null,
            (_, _) => Task.FromResult<IMcpClientSession>(session),
            httpTransportBuilder: options =>
            {
                capturedOptions = options;
                return new FakeClientTransport("sse-ide");
            });
        var server = CreateServer("ide", new McpSseIdeServerConfig("http://127.0.0.1:3030/sse", "VS Code", false));

        var connection = await connector.ConnectAsync("ide", server);

        Assert.IsType<ConnectedMcpServerConnection>(connection);
        Assert.NotNull(capturedOptions);
        Assert.Equal(HttpTransportMode.Sse, capturedOptions!.TransportMode);
        Assert.Equal("http://127.0.0.1:3030/sse", capturedOptions.Endpoint.ToString());
    }

    [Fact]
    public async Task ConnectAsync_UsesWebSocketIdeTransport()
    {
        var storage = new InMemoryMcpSecureStorage();
        var authState = new McpAuthStateService(storage);
        var transportFactory = new McpSdkHttpTransportFactory(storage, authState);
        var session = new RecordingMcpClientSession();
        Uri? capturedUri = null;
        IReadOnlyDictionary<string, string>? capturedHeaders = null;
        var connector = CreateConnector(
            transportFactory,
            null,
            null,
            (_, _) => Task.FromResult<IMcpClientSession>(session),
            webSocketTransportBuilder: (uri, headers) =>
            {
                capturedUri = uri;
                capturedHeaders = headers;
                return new FakeClientTransport("ws-ide");
            });
        var server = CreateServer("ide", new McpWebSocketIdeServerConfig("ws://127.0.0.1:3030", "VS Code", "token-123", false));

        var connection = await connector.ConnectAsync("ide", server);

        Assert.IsType<ConnectedMcpServerConnection>(connection);
        Assert.Equal("ws://127.0.0.1:3030/", capturedUri?.ToString());
        Assert.NotNull(capturedHeaders);
        Assert.Equal("token-123", capturedHeaders!["X-Claude-Code-Ide-Authorization"]);
    }

    [Fact]
    public async Task ConnectAsync_RetriesAfterProtectedResourceMismatchWhenOAuthBootstrapSucceeds()
    {
        var storage = new InMemoryMcpSecureStorage();
        var authState = new McpAuthStateService(storage);
        var transportFactory = new McpSdkHttpTransportFactory(storage, authState);
        var session = new RecordingMcpClientSession();
        var bootstrapCalls = 0;
        var connectAttempts = 0;
        var connector = CreateConnector(
            transportFactory,
            null,
            null,
            (_, _) =>
            {
                connectAttempts++;
                if (connectAttempts == 1)
                {
                    throw new InvalidOperationException("Resource URI in metadata (https://mcp.linear.app) does not match the expected URI (https://mcp.linear.app/mcp)");
                }

                return Task.FromResult<IMcpClientSession>(session);
            },
            oauthBootstrapper: (_, _) =>
            {
                bootstrapCalls++;
                return Task.FromResult(true);
            });
        var server = CreateServer("linear", new McpHttpServerConfig("https://mcp.linear.app/mcp", null, null, null));

        var connection = await connector.ConnectAsync("linear", server);

        var connected = Assert.IsType<ConnectedMcpServerConnection>(connection);
        Assert.Same(session, connected.Client);
        Assert.Equal(1, bootstrapCalls);
        Assert.Equal(2, connectAttempts);
    }

    private static SdkMcpClientConnector CreateConnector(
        McpSdkHttpTransportFactory transportFactory,
        McpNeedsAuthCache? cache,
        McpElicitationService? elicitationService,
        Func<IClientTransport, CancellationToken, Task<IMcpClientSession>> sessionFactory,
        Func<HttpClientTransportOptions, IClientTransport>? httpTransportBuilder = null,
        Func<Uri, IReadOnlyDictionary<string, string>?, IClientTransport>? webSocketTransportBuilder = null,
        Func<HttpClientTransportOptions, CancellationToken, Task<bool>>? oauthBootstrapper = null)
    {
        var constructor = typeof(SdkMcpClientConnector).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [
                typeof(McpSdkHttpTransportFactory),
                typeof(McpNeedsAuthCache),
                typeof(McpElicitationService),
                typeof(Func<HttpClientTransportOptions, IClientTransport>),
                typeof(Func<Uri, IReadOnlyDictionary<string, string>?, IClientTransport>),
                typeof(Func<IClientTransport, CancellationToken, Task<IMcpClientSession>>),
                typeof(Func<HttpClientTransportOptions, CancellationToken, Task<bool>>)
            ],
            modifiers: null);
        Assert.NotNull(constructor);

        return (SdkMcpClientConnector)constructor.Invoke(
            [
                transportFactory,
                cache,
                elicitationService,
                httpTransportBuilder ?? (Func<HttpClientTransportOptions, IClientTransport>)(options => new HttpClientTransport(options)),
                webSocketTransportBuilder ?? (Func<Uri, IReadOnlyDictionary<string, string>?, IClientTransport>)((uri, headers) => new FakeClientTransport(uri.ToString())),
                sessionFactory,
                oauthBootstrapper ?? (Func<HttpClientTransportOptions, CancellationToken, Task<bool>>)((_, _) => Task.FromResult(false))
            ]);
    }

    private static ScopedMcpServerConfig CreateServer(string name, McpServerConfig config)
    {
        return new ScopedMcpServerConfig(name, config, McpConfigScope.User);
    }

    private sealed class RecordingMcpClientSession : IMcpClientSession
    {
        public bool RequestHandlerRegistered { get; private set; }

        public bool CompletionHandlerRegistered { get; private set; }

        public bool DisposeCalled { get; private set; }

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
            DisposeCalled = true;
            return ValueTask.CompletedTask;
        }

        public void SetElicitationRequestHandler(Func<McpElicitationRequestContext, Task<McpElicitResult>> handler)
        {
            RequestHandlerRegistered = true;
        }

        public void SetElicitationCompletionHandler(Action<string> handler)
        {
            CompletionHandlerRegistered = true;
        }
    }

    private sealed class FakeClientTransport : IClientTransport
    {
        public FakeClientTransport(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
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
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "clawsharp-mcp-sdk-connector-tests",
                Guid.NewGuid().ToString("N"));
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
