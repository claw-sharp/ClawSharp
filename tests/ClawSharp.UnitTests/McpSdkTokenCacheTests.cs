// TS origin: ./services/mcp/auth.ts
using ModelContextProtocol.Authentication;
using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public sealed class McpSdkTokenCacheTests
{
    [Fact]
    public async Task StoreTokensAsync_PersistsTokensWithoutClobberingStoredClientInfo()
    {
        var storage = new InMemoryMcpSecureStorage();
        var authState = new McpAuthStateService(storage);
        var server = new McpHttpServerConfig("https://example.test/mcp", null, null, null);
        authState.SaveOAuthEntry(
            "server",
            server,
            new McpOAuthEntry(
                "server",
                "https://example.test/mcp",
                string.Empty,
                0,
                ClientId: "client-id",
                ClientSecret: "client-secret",
                DiscoveryState: new McpOAuthDiscoveryState("https://auth.example.test")));

        var cache = new McpSdkTokenCache("server", server, storage, authState);

        await cache.StoreTokensAsync(
            new TokenContainer
            {
                AccessToken = "access-token",
                RefreshToken = "refresh-token",
                ExpiresIn = 1200,
                Scope = "scope:read",
                TokenType = "Bearer",
                ObtainedAt = DateTimeOffset.UtcNow
            },
            CancellationToken.None);

        var entry = authState.GetOAuthEntry("server", server);
        Assert.NotNull(entry);
        Assert.Equal("access-token", entry!.AccessToken);
        Assert.Equal("refresh-token", entry.RefreshToken);
        Assert.Equal("scope:read", entry.Scope);
        Assert.Equal("client-id", entry.ClientId);
        Assert.Equal("client-secret", entry.ClientSecret);
        Assert.Equal("https://auth.example.test", entry.DiscoveryState?.AuthorizationServerUrl);
    }

    [Fact]
    public async Task GetTokensAsync_ReturnsBearerTokenContainerFromStoredEntry()
    {
        var storage = new InMemoryMcpSecureStorage();
        var authState = new McpAuthStateService(storage);
        var server = new McpSseServerConfig("https://example.test/sse", null, null, null);
        authState.SaveOAuthEntry(
            "server",
            server,
            new McpOAuthEntry(
                "server",
                "https://example.test/sse",
                "access-token",
                DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds(),
                RefreshToken: "refresh-token",
                Scope: "scope:read"));

        var cache = new McpSdkTokenCache("server", server, storage, authState);

        var tokens = await cache.GetTokensAsync(CancellationToken.None);

        Assert.NotNull(tokens);
        Assert.Equal("Bearer", tokens!.TokenType);
        Assert.Equal("access-token", tokens.AccessToken);
        Assert.Equal("refresh-token", tokens.RefreshToken);
        Assert.Equal("scope:read", tokens.Scope);
        Assert.True(tokens.ExpiresIn > 0);
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
