// TS origin: ./services/mcp/auth.ts
using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public sealed class McpAuthStateServiceTests
{
    [Fact]
    public void GetServerKey_IncludesRemoteConfigShape()
    {
        var first = McpAuthStateService.GetServerKey(
            "server",
            new McpHttpServerConfig(
                "https://example.test",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["x-api-key"] = "one"
                },
                null,
                null));
        var second = McpAuthStateService.GetServerKey(
            "server",
            new McpHttpServerConfig(
                "https://example.test",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["x-api-key"] = "two"
                },
                null,
                null));

        Assert.NotEqual(first, second);
        Assert.StartsWith("server|", first, StringComparison.Ordinal);
    }

    [Fact]
    public void SaveAndGetClientConfig_RoundTripsClientSecret()
    {
        var storage = new InMemoryMcpSecureStorage();
        var service = new McpAuthStateService(storage);
        var server = new McpSseServerConfig("https://example.test/sse", null, null, null);

        service.SaveMcpClientSecret("server", server, "secret-value");

        var config = service.GetMcpClientConfig("server", server);
        Assert.NotNull(config);
        Assert.Equal("secret-value", config!.ClientSecret);
    }

    [Fact]
    public void ClearMcpClientConfig_RemovesStoredValue()
    {
        var storage = new InMemoryMcpSecureStorage();
        var service = new McpAuthStateService(storage);
        var server = new McpHttpServerConfig("https://example.test", null, null, null);
        service.SaveMcpClientSecret("server", server, "secret-value");

        service.ClearMcpClientConfig("server", server);

        Assert.Null(service.GetMcpClientConfig("server", server));
    }

    [Fact]
    public void HasDiscoveryButNoToken_ReturnsTrueWhenDiscoveryExistsWithoutCredentials()
    {
        var storage = new InMemoryMcpSecureStorage();
        var service = new McpAuthStateService(storage);
        var server = new McpHttpServerConfig("https://example.test", null, null, null);
        service.SaveOAuthEntry(
            "server",
            server,
            new McpOAuthEntry(
                "server",
                "https://example.test",
                string.Empty,
                0,
                RefreshToken: string.Empty,
                DiscoveryState: new McpOAuthDiscoveryState("https://auth.example.test")));

        Assert.True(service.HasDiscoveryButNoToken("server", server));
    }

    [Fact]
    public void HasDiscoveryButNoToken_ReturnsFalseForXaaServers()
    {
        var storage = new InMemoryMcpSecureStorage();
        var service = new McpAuthStateService(storage);
        var server = new McpHttpServerConfig(
            "https://example.test",
            null,
            null,
            new McpOAuthConfig(null, null, null, true));
        service.SaveOAuthEntry(
            "server",
            server,
            new McpOAuthEntry(
                "server",
                "https://example.test",
                string.Empty,
                0,
                DiscoveryState: new McpOAuthDiscoveryState("https://auth.example.test")));

        Assert.False(service.HasDiscoveryButNoToken("server", server));
    }

    [Fact]
    public void ClearServerTokensFromLocalStorage_RemovesStoredEntry()
    {
        var storage = new InMemoryMcpSecureStorage();
        var service = new McpAuthStateService(storage);
        var server = new McpSseServerConfig("https://example.test/sse", null, null, null);
        service.SaveOAuthEntry(
            "server",
            server,
            new McpOAuthEntry("server", "https://example.test/sse", "access", 1000, "refresh"));

        service.ClearServerTokensFromLocalStorage("server", server);

        Assert.Null(service.GetOAuthEntry("server", server));
    }

    [Fact]
    public void ClearServerTokensFromLocalStorage_PreservesStepUpStateWhenRequested()
    {
        var storage = new InMemoryMcpSecureStorage();
        var service = new McpAuthStateService(storage);
        var server = new McpHttpServerConfig("https://example.test", null, null, null);
        service.SaveOAuthEntry(
            "server",
            server,
            new McpOAuthEntry(
                "server",
                "https://example.test",
                "access",
                1000,
                RefreshToken: "refresh",
                StepUpScope: "scope:write",
                DiscoveryState: new McpOAuthDiscoveryState(
                    "https://auth.example.test",
                    "https://resource.example.test")));

        service.ClearServerTokensFromLocalStorage("server", server, preserveStepUpState: true);

        var entry = service.GetOAuthEntry("server", server);
        Assert.NotNull(entry);
        Assert.Equal(string.Empty, entry!.AccessToken);
        Assert.Equal(0, entry.ExpiresAt);
        Assert.Null(entry.RefreshToken);
        Assert.Equal("scope:write", entry.StepUpScope);
        Assert.Equal("https://auth.example.test", entry.DiscoveryState?.AuthorizationServerUrl);
        Assert.Equal("https://resource.example.test", entry.DiscoveryState?.ResourceMetadataUrl);
    }

    [Fact]
    public void ClearServerTokensFromLocalStorage_DoesNotPreserveStepUpStateByDefault()
    {
        var storage = new InMemoryMcpSecureStorage();
        var service = new McpAuthStateService(storage);
        var server = new McpHttpServerConfig("https://example.test", null, null, null);
        service.SaveOAuthEntry(
            "server",
            server,
            new McpOAuthEntry(
                "server",
                "https://example.test",
                "access",
                1000,
                RefreshToken: "refresh",
                StepUpScope: "scope:write",
                DiscoveryState: new McpOAuthDiscoveryState("https://auth.example.test")));

        service.ClearServerTokensFromLocalStorage("server", server);

        Assert.Null(service.GetOAuthEntry("server", server));
    }

    [Fact]
    public void SaveDiscoveryState_PersistsUrlsWithoutClobberingTokens()
    {
        var storage = new InMemoryMcpSecureStorage();
        var service = new McpAuthStateService(storage);
        var server = new McpHttpServerConfig("https://example.test", null, null, null);
        service.SaveOAuthEntry(
            "server",
            server,
            new McpOAuthEntry(
                "server",
                "https://example.test",
                "access-token",
                1234,
                RefreshToken: "refresh-token"));

        service.SaveDiscoveryState(
            "server",
            server,
            new McpOAuthDiscoveryState(
                "https://auth.example.test",
                "https://resource.example.test"));

        var entry = service.GetOAuthEntry("server", server);
        Assert.NotNull(entry);
        Assert.Equal("access-token", entry!.AccessToken);
        Assert.Equal("refresh-token", entry.RefreshToken);
        Assert.Equal(1234, entry.ExpiresAt);
        Assert.Equal("https://auth.example.test", entry.DiscoveryState?.AuthorizationServerUrl);
        Assert.Equal("https://resource.example.test", entry.DiscoveryState?.ResourceMetadataUrl);
    }

    [Fact]
    public void GetDiscoveryState_ReturnsStoredDiscoveryState()
    {
        var storage = new InMemoryMcpSecureStorage();
        var service = new McpAuthStateService(storage);
        var server = new McpHttpServerConfig("https://example.test", null, null, null);
        service.SaveDiscoveryState(
            "server",
            server,
            new McpOAuthDiscoveryState(
                "https://auth.example.test",
                "https://resource.example.test"));

        var state = service.GetDiscoveryState("server", server);

        Assert.NotNull(state);
        Assert.Equal("https://auth.example.test", state!.AuthorizationServerUrl);
        Assert.Equal("https://resource.example.test", state.ResourceMetadataUrl);
    }

    [Fact]
    public void PersistStepUpScope_UpdatesExistingEntry()
    {
        var storage = new InMemoryMcpSecureStorage();
        var service = new McpAuthStateService(storage);
        var server = new McpHttpServerConfig("https://example.test", null, null, null);
        service.SaveOAuthEntry(
            "server",
            server,
            new McpOAuthEntry("server", "https://example.test", "access", 1000));

        service.PersistStepUpScope("server", server, "scope:write");

        var entry = service.GetOAuthEntry("server", server);
        Assert.NotNull(entry);
        Assert.Equal("scope:write", entry!.StepUpScope);
    }

    [Theory]
    [InlineData("client")]
    [InlineData("tokens")]
    [InlineData("discovery")]
    [InlineData("all")]
    public void InvalidateCredentials_AppliesScopedMutation(string scope)
    {
        var storage = new InMemoryMcpSecureStorage();
        var service = new McpAuthStateService(storage);
        var server = new McpHttpServerConfig("https://example.test", null, null, null);
        service.SaveOAuthEntry(
            "server",
            server,
            new McpOAuthEntry(
                "server",
                "https://example.test",
                "access",
                1000,
                RefreshToken: "refresh",
                Scope: "scope:read",
                ClientId: "client-id",
                ClientSecret: "client-secret",
                StepUpScope: "scope:write",
                DiscoveryState: new McpOAuthDiscoveryState(
                    "https://auth.example.test",
                    "https://resource.example.test")));

        service.InvalidateCredentials("server", server, scope);

        var entry = service.GetOAuthEntry("server", server);
        switch (scope)
        {
            case "client":
                Assert.NotNull(entry);
                Assert.Null(entry!.ClientId);
                Assert.Null(entry.ClientSecret);
                Assert.Equal("access", entry.AccessToken);
                break;
            case "tokens":
                Assert.NotNull(entry);
                Assert.Equal(string.Empty, entry!.AccessToken);
                Assert.Null(entry.RefreshToken);
                Assert.Equal(0, entry.ExpiresAt);
                Assert.Equal("client-id", entry.ClientId);
                break;
            case "discovery":
                Assert.NotNull(entry);
                Assert.Null(entry!.DiscoveryState);
                Assert.Null(entry.StepUpScope);
                Assert.Equal("access", entry.AccessToken);
                break;
            case "all":
                Assert.Null(entry);
                break;
        }
    }

    [Fact]
    public void ClearStoredClientRegistration_ClearsOnlyClientFields()
    {
        var storage = new InMemoryMcpSecureStorage();
        var service = new McpAuthStateService(storage);
        var server = new McpHttpServerConfig("https://example.test", null, null, null);
        service.SaveOAuthEntry(
            "server",
            server,
            new McpOAuthEntry(
                "server",
                "https://example.test",
                "access",
                1000,
                RefreshToken: "refresh",
                ClientId: "client-id",
                ClientSecret: "client-secret"));

        service.ClearStoredClientRegistration("server", server);

        var entry = service.GetOAuthEntry("server", server);
        Assert.NotNull(entry);
        Assert.Null(entry!.ClientId);
        Assert.Null(entry.ClientSecret);
        Assert.Equal("access", entry.AccessToken);
        Assert.Equal("refresh", entry.RefreshToken);
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
