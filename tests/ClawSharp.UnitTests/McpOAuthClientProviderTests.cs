using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public sealed class McpOAuthClientProviderTests
{
    [Fact]
    public void ClientInformation_UsesStoredOAuthEntryAndFallbackClientSecret()
    {
        var storage = new InMemoryMcpSecureStorage();
        var authState = new McpAuthStateService(storage);
        var server = new McpHttpServerConfig("https://example.test", null, null, null);
        authState.SaveOAuthEntry(
            "server",
            server,
            new McpOAuthEntry(
                "server",
                "https://example.test",
                "access",
                1000,
                ClientId: "client-id"));
        authState.SaveMcpClientSecret("server", server, "stored-secret");
        var provider = new McpOAuthClientProvider("server", server, storage, authState);

        var client = provider.ClientInformation();

        Assert.NotNull(client);
        Assert.Equal("client-id", client!.ClientId);
        Assert.Equal("stored-secret", client.ClientSecret);
    }

    [Fact]
    public void SaveClientInformation_PreservesExistingTokenState()
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
                1234,
                RefreshToken: "refresh-token",
                Scope: "scope:read"));
        var provider = new McpOAuthClientProvider("server", server, storage, authState);

        provider.SaveClientInformation(new McpOAuthClientInformation("client-id", "client-secret"));

        var entry = authState.GetOAuthEntry("server", server);
        Assert.NotNull(entry);
        Assert.Equal("access-token", entry!.AccessToken);
        Assert.Equal("refresh-token", entry.RefreshToken);
        Assert.Equal(1234, entry.ExpiresAt);
        Assert.Equal("client-id", entry.ClientId);
        Assert.Equal("client-secret", entry.ClientSecret);
    }

    [Fact]
    public async Task TokensAsync_OmitsRefreshTokenWhenStepUpIsPending()
    {
        var storage = new InMemoryMcpSecureStorage();
        var authState = new McpAuthStateService(storage);
        var server = new McpHttpServerConfig("https://example.test", null, null, null);
        authState.SaveOAuthEntry(
            "server",
            server,
            new McpOAuthEntry(
                "server",
                "https://example.test",
                "access-token",
                DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds(),
                RefreshToken: "refresh-token",
                Scope: "scope:read"));
        var provider = new McpOAuthClientProvider("server", server, storage, authState);
        provider.MarkStepUpPending("scope:write");

        var tokens = await provider.TokensAsync();

        Assert.NotNull(tokens);
        Assert.Equal("access-token", tokens!.AccessToken);
        Assert.Null(tokens.RefreshToken);
        Assert.Equal("scope:read", tokens.Scope);
    }

    [Fact]
    public async Task SaveTokens_ResetsPendingStepUpAndPersistsTokenValues()
    {
        var storage = new InMemoryMcpSecureStorage();
        var authState = new McpAuthStateService(storage);
        var server = new McpHttpServerConfig("https://example.test", null, null, null);
        authState.SaveOAuthEntry(
            "server",
            server,
            new McpOAuthEntry(
                "server",
                "https://example.test",
                "old-access",
                1000,
                RefreshToken: "old-refresh",
                Scope: "scope:read",
                ClientId: "client-id"));
        var provider = new McpOAuthClientProvider("server", server, storage, authState);
        provider.MarkStepUpPending("scope:write");

        provider.SaveTokens(new McpOAuthTokens("new-access", "new-refresh", 60, "scope:write"));

        var entry = authState.GetOAuthEntry("server", server);
        Assert.NotNull(entry);
        Assert.Equal("new-access", entry!.AccessToken);
        Assert.Equal("new-refresh", entry.RefreshToken);
        Assert.Equal("scope:write", entry.Scope);
        Assert.True(entry.ExpiresAt > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        var tokens = await provider.TokensAsync();
        Assert.NotNull(tokens);
        Assert.Equal("new-refresh", tokens!.RefreshToken);
    }

    [Fact]
    public void PersistAuthorizationUrl_StoresScopeAsStepUpScope()
    {
        var storage = new InMemoryMcpSecureStorage();
        var authState = new McpAuthStateService(storage);
        var server = new McpHttpServerConfig("https://example.test", null, null, null);
        authState.SaveOAuthEntry(
            "server",
            server,
            new McpOAuthEntry("server", "https://example.test", string.Empty, 0));
        var provider = new McpOAuthClientProvider("server", server, storage, authState);

        provider.PersistAuthorizationUrl("https://auth.example.test/authorize?scope=scope%3Awrite&state=abc");

        Assert.Equal("https://auth.example.test/authorize?scope=scope%3Awrite&state=abc", provider.AuthorizationUrl);
        Assert.Equal("scope:write", authState.GetOAuthEntry("server", server)!.StepUpScope);
    }

    [Fact]
    public void CodeVerifier_CanBeSavedReadAndInvalidated()
    {
        var storage = new InMemoryMcpSecureStorage();
        var authState = new McpAuthStateService(storage);
        var server = new McpHttpServerConfig("https://example.test", null, null, null);
        var provider = new McpOAuthClientProvider("server", server, storage, authState);
        provider.SaveCodeVerifier("verifier");

        Assert.Equal("verifier", provider.GetCodeVerifier());

        provider.InvalidateCredentials("verifier");

        Assert.Throws<InvalidOperationException>(() => provider.GetCodeVerifier());
    }

    [Fact]
    public void DiscoveryState_RoundTripsThroughAuthStateStorage()
    {
        var storage = new InMemoryMcpSecureStorage();
        var authState = new McpAuthStateService(storage);
        var server = new McpSseServerConfig("https://example.test/sse", null, null, null);
        var provider = new McpOAuthClientProvider("server", server, storage, authState);

        provider.SaveDiscoveryState(new McpOAuthDiscoveryState("https://auth.example.test", "https://resource.example.test"));

        var state = provider.DiscoveryState();
        Assert.NotNull(state);
        Assert.Equal("https://auth.example.test", state!.AuthorizationServerUrl);
        Assert.Equal("https://resource.example.test", state.ResourceMetadataUrl);
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
