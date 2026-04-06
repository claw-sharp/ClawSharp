// TS origin: ./services/mcp/auth.ts, ./services/mcp/client.ts, ./constants/oauth.ts
using System.Reflection;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public sealed class McpSdkHttpTransportFactoryTests
{
    [Fact]
    public async Task CreateAsync_MapsHttpServerIntoSdkTransportOptions()
    {
        var storage = new InMemoryMcpSecureStorage();
        var authState = new McpAuthStateService(storage);
        var server = new ScopedMcpServerConfig(
            "server",
            new McpHttpServerConfig(
                "https://example.test/mcp",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["x-api-key"] = "secret"
                },
                null,
                new McpOAuthConfig("configured-client", 43123, null, null)),
            McpConfigScope.User);
        authState.SaveOAuthEntry(
            "server",
            server.Config,
            new McpOAuthEntry(
                "server",
                "https://example.test/mcp",
                string.Empty,
                0,
                ClientId: "stored-client",
                ClientSecret: "stored-secret"));

        var factory = new McpSdkHttpTransportFactory(storage, authState);

        var options = await factory.CreateAsync("server", server, skipBrowserOpen: true);

        Assert.Equal(new Uri("https://example.test/mcp"), options.Endpoint);
        Assert.Equal(HttpTransportMode.StreamableHttp, options.TransportMode);
        Assert.NotNull(options.AdditionalHeaders);
        Assert.Equal("secret", options.AdditionalHeaders!["x-api-key"]);
        Assert.NotNull(options.OAuth);
        Assert.Equal("configured-client", options.OAuth!.ClientId);
        Assert.Equal("stored-secret", options.OAuth.ClientSecret);
        Assert.Equal(new Uri("http://localhost:43123/callback"), options.OAuth.RedirectUri);
        Assert.IsType<McpSdkTokenCache>(options.OAuth.TokenCache);
        Assert.NotNull(options.OAuth.DynamicClientRegistration);
        Assert.Equal("ClawSharp (server)", options.OAuth.DynamicClientRegistration!.ClientName);

        await options.OAuth.DynamicClientRegistration.ResponseDelegate!(
            new DynamicClientRegistrationResponse
            {
                ClientId = "registered-client",
                ClientSecret = "registered-secret"
            },
            CancellationToken.None);

        var stored = authState.GetOAuthEntry("server", server.Config);
        Assert.Equal("registered-client", stored!.ClientId);
        Assert.Equal("registered-secret", stored.ClientSecret);
    }

    [Fact]
    public async Task CreateAsync_UsesClientMetadataDocumentEnvOverride()
    {
        var original = Environment.GetEnvironmentVariable("MCP_OAUTH_CLIENT_METADATA_URL");
        Environment.SetEnvironmentVariable("MCP_OAUTH_CLIENT_METADATA_URL", "https://override.example.test/client-metadata");

        try
        {
            var storage = new InMemoryMcpSecureStorage();
            var authState = new McpAuthStateService(storage);
            var factory = new McpSdkHttpTransportFactory(storage, authState);
            var server = new ScopedMcpServerConfig(
                "server",
                new McpSseServerConfig("https://example.test/sse", null, null, null),
                McpConfigScope.User);

            var options = await factory.CreateAsync("server", server, skipBrowserOpen: true);

            Assert.Equal(
                new Uri("https://override.example.test/client-metadata"),
                options.OAuth!.ClientMetadataDocumentUri);
            Assert.Equal(HttpTransportMode.Sse, options.TransportMode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MCP_OAUTH_CLIENT_METADATA_URL", original);
        }
    }

    [Fact]
    public async Task CreateAsync_UsesConfiguredAuthServerMetadataUrlToSelectAuthServerAndPersistDiscoveryState()
    {
        var storage = new InMemoryMcpSecureStorage();
        var authState = new McpAuthStateService(storage);
        var fetchedUris = new List<Uri>();
        var factory = CreateFactory(
            storage,
            authState,
            metadataDocumentFetcherAsync: (uri, cancellationToken) =>
            {
                fetchedUris.Add(uri);
                return Task.FromResult(
                    """
                    {
                      "issuer": "https://issuer.example.test",
                      "authorization_endpoint": "https://issuer.example.test/authorize",
                      "token_endpoint": "https://issuer.example.test/token"
                    }
                    """);
            });
        var server = new ScopedMcpServerConfig(
            "server",
            new McpHttpServerConfig(
                "https://example.test/mcp",
                null,
                null,
                new McpOAuthConfig(null, null, "https://metadata.example.test/.well-known/oauth-authorization-server", null)),
            McpConfigScope.User);

        var options = await factory.CreateAsync("server", server, skipBrowserOpen: true);

        Assert.Equal(
            new Uri("https://metadata.example.test/.well-known/oauth-authorization-server"),
            Assert.Single(fetchedUris));
        Assert.NotNull(options.OAuth);
        var selected = options.OAuth!.AuthServerSelector!(
            [
                new Uri("https://other.example.test"),
                new Uri("https://issuer.example.test")
            ]);
        Assert.Equal(new Uri("https://issuer.example.test"), selected);
        var discoveryState = authState.GetDiscoveryState("server", server.Config);
        Assert.NotNull(discoveryState);
        Assert.Equal("https://issuer.example.test/", discoveryState!.AuthorizationServerUrl);
    }

    [Fact]
    public async Task CreateAsync_RejectsNonHttpsConfiguredAuthServerMetadataUrl()
    {
        var storage = new InMemoryMcpSecureStorage();
        var authState = new McpAuthStateService(storage);
        var factory = new McpSdkHttpTransportFactory(storage, authState);
        var server = new ScopedMcpServerConfig(
            "server",
            new McpSseServerConfig(
                "https://example.test/sse",
                null,
                null,
                new McpOAuthConfig(null, null, "http://metadata.example.test/oauth", null)),
            McpConfigScope.User);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.CreateAsync("server", server, skipBrowserOpen: true));

        Assert.Equal(
            "authServerMetadataUrl must use https:// (got: http://metadata.example.test/oauth)",
            error.Message);
    }

    private static McpSdkHttpTransportFactory CreateFactory(
        IMcpSecureStorage storage,
        McpAuthStateService authStateService,
        Func<Uri, CancellationToken, Task<string>> metadataDocumentFetcherAsync)
    {
        var constructor = typeof(McpSdkHttpTransportFactory).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [
                typeof(IMcpSecureStorage),
                typeof(McpAuthStateService),
                typeof(Func<Uri, CancellationToken, Task>),
                typeof(Func<Uri, CancellationToken, Task<string>>)
            ],
            modifiers: null);
        Assert.NotNull(constructor);

        return (McpSdkHttpTransportFactory)constructor.Invoke(
            [
                storage,
                authStateService,
                null!,
                metadataDocumentFetcherAsync
            ]);
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
}
