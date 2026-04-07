// Tests for:
//   Milestone 139: McpTokenRevocationService (revokeServerTokens parity)
//   Milestone 140: McpChannelPermissions (channelPermissions.ts parity)
//   Milestone 141: McpConnectionHealthUtilities / McpConnectionHealthMonitor

using System.Net;
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using Xunit;

namespace ClawSharp.UnitTests;

// ---------------------------------------------------------------------------
// Milestone 139: McpTokenRevocationService
// ---------------------------------------------------------------------------

public sealed class McpTokenRevocationServiceTests
{
    private static McpAuthStateService BuildAuthStateService(McpSecureStorageData? data = null)
    {
        var storage = new FakeMcpSecureStorage(data ?? new McpSecureStorageData());
        return new McpAuthStateService(storage);
    }

    [Fact]
    public async Task RevokeServerTokens_NoExistingEntry_ReturnsWithoutError()
    {
        var authStateService = BuildAuthStateService();
        var sut = new McpTokenRevocationService(authStateService, new HttpClient(new NoOpHandler()));
        var config = new McpHttpServerConfig("https://example.com/mcp", null, null, null);

        // Should complete without error even when no entry exists
        await sut.RevokeServerTokensAsync("server1", config);
    }

    [Fact]
    public async Task RevokeServerTokens_ClearsLocalTokensAlways()
    {
        var serverConfig = new McpHttpServerConfig("https://example.com/mcp", null, null, null);
        var serverKey = McpAuthStateService.GetServerKey("server1", serverConfig);
        var initialData = new McpSecureStorageData
        {
            McpOAuth = new Dictionary<string, McpOAuthEntry>(StringComparer.Ordinal)
            {
                [serverKey] = new McpOAuthEntry("server1", "https://example.com/mcp", "tok-access", 9999)
                {
                    RefreshToken = "tok-refresh"
                }
            }
        };

        var storage = new FakeMcpSecureStorage(initialData);
        var authStateService = new McpAuthStateService(storage);
        var sut = new McpTokenRevocationService(authStateService, new HttpClient(new NoOpHandler()));

        await sut.RevokeServerTokensAsync("server1", serverConfig);

        // Local tokens must be cleared
        var remaining = storage.Read()?.McpOAuth?.GetValueOrDefault(serverKey);
        Assert.Null(remaining);
    }

    [Fact]
    public async Task RevokeServerTokens_WithRevocationEndpoint_AttemptsPost()
    {
        var wasCalled = false;
        var serverConfig = new McpHttpServerConfig("https://example.com/mcp", null, null, null);
        var serverKey = McpAuthStateService.GetServerKey("server1", serverConfig);
        var initialData = new McpSecureStorageData
        {
            McpOAuth = new Dictionary<string, McpOAuthEntry>(StringComparer.Ordinal)
            {
                [serverKey] = new McpOAuthEntry("server1", "https://example.com/mcp", "tok-access", 9999)
                {
                    RefreshToken = "tok-refresh"
                }
            }
        };

        var storage = new FakeMcpSecureStorage(initialData);
        var authStateService = new McpAuthStateService(storage);
        var handler = new CapturingHandler(response: new HttpResponseMessage(HttpStatusCode.OK));
        handler.OnRequest = _ => wasCalled = true;
        var sut = new McpTokenRevocationService(authStateService, new HttpClient(handler));

        await sut.RevokeServerTokensAsync(
            "server1", serverConfig,
            revocationEndpointOverride: "https://auth.example.com/revoke");

        Assert.True(wasCalled, "Expected HTTP POST to revocation endpoint");
    }

    [Fact]
    public async Task RevokeToken_ClientSecretBasic_SendsBasicAuthHeader()
    {
        var authStateService = BuildAuthStateService();
        string? capturedAuthHeader = null;
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        handler.OnRequest = req =>
            capturedAuthHeader = req.Headers.Authorization?.ToString();

        var sut = new McpTokenRevocationService(authStateService, new HttpClient(handler));

        await sut.RevokeTokenAsync(
            "server1",
            "https://auth.example.com/revoke",
            "the-token",
            "access_token",
            clientId: "cid",
            clientSecret: "csec",
            accessToken: null,
            authMethod: "client_secret_basic");

        Assert.NotNull(capturedAuthHeader);
        Assert.StartsWith("Basic ", capturedAuthHeader, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RevokeToken_ClientSecretPost_IncludesClientCredentialsInBody()
    {
        var authStateService = BuildAuthStateService();
        string? capturedBody = null;
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        handler.OnRequest = async req =>
            capturedBody = await req.Content!.ReadAsStringAsync();

        var sut = new McpTokenRevocationService(authStateService, new HttpClient(handler));

        await sut.RevokeTokenAsync(
            "server1",
            "https://auth.example.com/revoke",
            "the-token",
            "access_token",
            clientId: "cid",
            clientSecret: "csec",
            accessToken: null,
            authMethod: "client_secret_post");

        Assert.NotNull(capturedBody);
        Assert.Contains("client_id=cid", capturedBody, StringComparison.Ordinal);
        Assert.Contains("client_secret=csec", capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RevokeToken_On401_RetriesWithBearerAuth()
    {
        var authStateService = BuildAuthStateService();
        var callCount = 0;
        string? secondAuthHeader = null;

        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        handler.OnRequest = req =>
        {
            callCount++;
            if (callCount == 2)
            {
                secondAuthHeader = req.Headers.Authorization?.ToString();
            }
        };

        var sut = new McpTokenRevocationService(authStateService, new HttpClient(handler));

        await sut.RevokeTokenAsync(
            "server1",
            "https://auth.example.com/revoke",
            "the-token",
            "access_token",
            clientId: "cid",
            clientSecret: null,
            accessToken: "bearer-tok");

        Assert.Equal(2, callCount);
        Assert.Equal("Bearer bearer-tok", secondAuthHeader);
    }

    // Helpers

    private sealed class FakeMcpSecureStorage : IMcpSecureStorage
    {
        private McpSecureStorageData _data;
        public FakeMcpSecureStorage(McpSecureStorageData data) => _data = data;
        public McpSecureStorageData? Read() => _data;
        public Task<McpSecureStorageData?> ReadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<McpSecureStorageData?>(_data);
        public void Update(McpSecureStorageData data) => _data = data;
        public bool Delete() => true;
    }

    private sealed class NoOpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public Action<HttpRequestMessage>? OnRequest { get; set; }

        public CapturingHandler(HttpResponseMessage response)
            => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            OnRequest?.Invoke(request);
            return Task.FromResult(_response);
        }
    }
}

// ---------------------------------------------------------------------------
// Milestone 140: McpChannelPermissions
// ---------------------------------------------------------------------------

public sealed class McpChannelPermissionsTests
{
    // --- shortRequestId ---

    [Fact]
    public void ShortRequestId_ReturnsFiveCharString()
    {
        var id = McpChannelPermissions.ShortRequestId("toolu_abc123XYZ");
        Assert.Equal(5, id.Length);
    }

    [Fact]
    public void ShortRequestId_UsesOnlyAlphabetChars()
    {
        const string alphabet = "abcdefghijkmnopqrstuvwxyz";
        for (var i = 0; i < 50; i++)
        {
            var input = $"toolu_{i:X8}";
            var result = McpChannelPermissions.ShortRequestId(input);
            Assert.Equal(5, result.Length);
            foreach (var ch in result)
            {
                Assert.Contains(ch, alphabet);
            }
        }
    }

    [Fact]
    public void ShortRequestId_NoBlocklistedSubstrings()
    {
        // Generate 500 IDs and verify none contain blocklisted substrings that
        // the salt-retry logic should eliminate.
        var blocklisted = new[] { "fuck", "shit", "cunt", "anus" };
        for (var i = 0; i < 500; i++)
        {
            var id = McpChannelPermissions.ShortRequestId($"toolu_test_{i}");
            foreach (var bad in blocklisted)
            {
                Assert.DoesNotContain(bad, id, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void ShortRequestId_Deterministic_SameInputSameOutput()
    {
        var first = McpChannelPermissions.ShortRequestId("toolu_stable");
        var second = McpChannelPermissions.ShortRequestId("toolu_stable");
        Assert.Equal(first, second);
    }

    // --- truncateForPreview ---

    [Fact]
    public void TruncateForPreview_ShortValue_ReturnsFullJson()
    {
        var result = McpChannelPermissions.TruncateForPreview(new { key = "value" });
        Assert.Contains("key", result, StringComparison.Ordinal);
        Assert.True(result.Length <= 200 + 1); // no ellipsis
    }

    [Fact]
    public void TruncateForPreview_LongValue_TruncatesAt200WithEllipsis()
    {
        var bigObj = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < 30; i++)
        {
            bigObj[$"field{i}"] = $"value_{i}_padddddddddddddding";
        }

        var result = McpChannelPermissions.TruncateForPreview(bigObj);
        Assert.True(result.Length <= 201); // 200 chars + "…"
        Assert.EndsWith("…", result, StringComparison.Ordinal);
    }

    [Fact]
    public void TruncateForPreview_Null_ReturnsJsonNull()
    {
        var result = McpChannelPermissions.TruncateForPreview(null);
        Assert.Equal("null", result);
    }

    // --- filterPermissionRelayClients ---

    [Fact]
    public void FilterPermissionRelayClients_ReturnsOnlyConnectedAllowlistedWithBothCapabilities()
    {
        var clients = new[]
        {
            new FakeConnection("connected", "tg", new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["experimental"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["claude/channel"] = true,
                    ["claude/channel/permission"] = true
                }
            }),
            new FakeConnection("connected", "disc", new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["experimental"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["claude/channel"] = true
                    // missing claude/channel/permission
                }
            }),
            new FakeConnection("needs-auth", "slack", new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["experimental"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["claude/channel"] = true,
                    ["claude/channel/permission"] = true
                }
            }),
        };

        var allowlist = new HashSet<string>(StringComparer.Ordinal) { "tg", "disc", "slack" };
        var result = McpChannelPermissions.FilterPermissionRelayClients(clients, name => allowlist.Contains(name));

        Assert.Single(result);
        Assert.Equal("tg", result[0].Name);
    }

    [Fact]
    public void FilterPermissionRelayClients_ExcludesNonAllowlisted()
    {
        var clients = new[]
        {
            new FakeConnection("connected", "tg", new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["experimental"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["claude/channel"] = true,
                    ["claude/channel/permission"] = true
                }
            }),
        };

        var result = McpChannelPermissions.FilterPermissionRelayClients(clients, _ => false);
        Assert.Empty(result);
    }

    // --- createChannelPermissionCallbacks ---

    [Fact]
    public void CreateChannelPermissionCallbacks_OnResponse_ThenResolve_InvokesHandler()
    {
        var callbacks = McpChannelPermissions.CreateChannelPermissionCallbacks();
        ChannelPermissionResponse? received = null;
        callbacks.OnResponse("abcde", r => received = r);

        var resolved = callbacks.Resolve("abcde", "allow", "plugin:telegram:tg");

        Assert.True(resolved);
        Assert.NotNull(received);
        Assert.Equal("allow", received.Behavior);
        Assert.Equal("plugin:telegram:tg", received.FromServer);
    }

    [Fact]
    public void CreateChannelPermissionCallbacks_Resolve_ReturnsFalseForUnknownId()
    {
        var callbacks = McpChannelPermissions.CreateChannelPermissionCallbacks();
        var resolved = callbacks.Resolve("xxxxx", "allow", "server");
        Assert.False(resolved);
    }

    [Fact]
    public void CreateChannelPermissionCallbacks_Unsubscribe_StopsDelivery()
    {
        var callbacks = McpChannelPermissions.CreateChannelPermissionCallbacks();
        var invoked = false;
        var unsubscribe = callbacks.OnResponse("abcde", _ => invoked = true);
        unsubscribe(); // unsubscribe before resolve

        callbacks.Resolve("abcde", "allow", "server");
        Assert.False(invoked);
    }

    [Fact]
    public void CreateChannelPermissionCallbacks_Resolve_IsCaseInsensitive()
    {
        var callbacks = McpChannelPermissions.CreateChannelPermissionCallbacks();
        ChannelPermissionResponse? received = null;
        callbacks.OnResponse("ABCDE", r => received = r);

        var resolved = callbacks.Resolve("abcde", "deny", "server");

        Assert.True(resolved);
        Assert.NotNull(received);
        Assert.Equal("deny", received.Behavior);
    }

    [Fact]
    public void CreateChannelPermissionCallbacks_DuplicateResolve_OnlyFiresOnce()
    {
        var callbacks = McpChannelPermissions.CreateChannelPermissionCallbacks();
        var count = 0;
        callbacks.OnResponse("abcde", _ => count++);

        callbacks.Resolve("abcde", "allow", "server");
        callbacks.Resolve("abcde", "allow", "server"); // duplicate

        Assert.Equal(1, count);
    }

    private sealed record FakeConnection(string Type, string Name, IReadOnlyDictionary<string, object?> Capabilities)
        : IMcpPermissionRelayConnection;
}

// ---------------------------------------------------------------------------
// Milestone 141: McpConnectionHealthUtilities / McpConnectionHealthMonitor
// ---------------------------------------------------------------------------

public sealed class McpConnectionHealthUtilitiesTests
{
    // --- GetConnectionTimeoutMs ---

    [Fact]
    public void GetConnectionTimeoutMs_DefaultsTo30000()
    {
        Environment.SetEnvironmentVariable("MCP_TIMEOUT", null);
        Assert.Equal(30_000, McpConnectionHealthUtilities.GetConnectionTimeoutMs());
    }

    [Fact]
    public void GetConnectionTimeoutMs_ReadsEnvVar()
    {
        Environment.SetEnvironmentVariable("MCP_TIMEOUT", "5000");
        try
        {
            Assert.Equal(5000, McpConnectionHealthUtilities.GetConnectionTimeoutMs());
        }
        finally
        {
            Environment.SetEnvironmentVariable("MCP_TIMEOUT", null);
        }
    }

    // --- IsTerminalConnectionError ---

    [Theory]
    [InlineData("ECONNRESET")]
    [InlineData("ETIMEDOUT")]
    [InlineData("EPIPE")]
    [InlineData("EHOSTUNREACH")]
    [InlineData("ECONNREFUSED")]
    [InlineData("Body Timeout Error")]
    [InlineData("terminated")]
    [InlineData("SSE stream disconnected")]
    [InlineData("Failed to reconnect SSE stream")]
    public void IsTerminalConnectionError_MatchesKnownPatterns(string message)
    {
        Assert.True(McpConnectionHealthUtilities.IsTerminalConnectionError(message));
    }

    [Fact]
    public void IsTerminalConnectionError_ReturnsFalseForTransientMessage()
    {
        Assert.False(McpConnectionHealthUtilities.IsTerminalConnectionError("transient retry"));
    }

    // --- IsSseReconnectionExhausted ---

    [Fact]
    public void IsSseReconnectionExhausted_MatchesPattern()
    {
        Assert.True(McpConnectionHealthUtilities.IsSseReconnectionExhausted(
            "Maximum reconnection attempts exceeded"));
    }

    [Fact]
    public void IsSseReconnectionExhausted_DoesNotMatchUnrelated()
    {
        Assert.False(McpConnectionHealthUtilities.IsSseReconnectionExhausted("Connection lost"));
    }

    // --- ConnectWithTimeoutAsync ---

    [Fact]
    public async Task ConnectWithTimeoutAsync_CompletesWithinTimeout_DoesNotThrow()
    {
        await McpConnectionHealthUtilities.ConnectWithTimeoutAsync(
            "server1",
            _ => Task.CompletedTask);
    }
}

public sealed class McpConnectionHealthMonitorTests
{
    private readonly CapturingSink _sink = new();

    private McpConnectionHealthMonitor BuildMonitor(string transport = "http")
        => new("server1", transport, _sink);

    // --- Session expiry ---

    [Fact]
    public void OnError_SessionExpiredOnHttp_TriggersClose()
    {
        var monitor = BuildMonitor("http");
        var closeCalled = false;
        var error = new Exception("HTTP 404 with {\"code\":-32001,\"message\":\"Session not found\"}");
        error.Data["code"] = 404;

        var shouldReconnect = monitor.OnError(error, _ => closeCalled = true);

        Assert.True(shouldReconnect);
        Assert.True(closeCalled);
        Assert.Equal("server1", _sink.LastSessionExpiredServer);
    }

    [Fact]
    public void OnError_SessionExpiredOnHttp_GuardedAgainstReEntry()
    {
        var monitor = BuildMonitor("http");
        var closeCallCount = 0;
        var error = new Exception("HTTP 404 with {\"code\":-32001,\"message\":\"Session not found\"}");
        error.Data["code"] = 404;

        monitor.OnError(error, _ => closeCallCount++);
        monitor.OnError(error, _ => closeCallCount++); // second call should be guarded

        Assert.Equal(1, closeCallCount);
    }

    // --- SSE reconnection exhausted ---

    [Fact]
    public void OnError_SseReconnectionExhausted_TriggersClose()
    {
        var monitor = BuildMonitor("sse");
        var closeCalled = false;
        var error = new Exception("Maximum reconnection attempts reached");

        var shouldReconnect = monitor.OnError(error, _ => closeCalled = true);

        Assert.True(shouldReconnect);
        Assert.True(closeCalled);
    }

    // --- Terminal error threshold ---

    [Fact]
    public void OnError_TerminalErrors_TriggersAfterMaxThreshold()
    {
        var monitor = BuildMonitor("http");
        var closeCallCount = 0;
        var error = new Exception("ECONNRESET: connection reset");

        // MaxErrorsBeforeReconnect = 3
        monitor.OnError(error, _ => closeCallCount++);
        monitor.OnError(error, _ => closeCallCount++);
        Assert.Equal(0, closeCallCount); // not yet triggered

        monitor.OnError(error, _ => closeCallCount++);
        Assert.Equal(1, closeCallCount); // threshold reached
    }

    [Fact]
    public void OnError_NonTerminalErrorResetsCounter()
    {
        var monitor = BuildMonitor("http");
        var closeCallCount = 0;
        var terminalError = new Exception("ECONNRESET");
        var transientError = new Exception("some transient issue");

        monitor.OnError(terminalError, _ => closeCallCount++); // count = 1
        monitor.OnError(transientError, _ => closeCallCount++); // resets
        monitor.OnError(terminalError, _ => closeCallCount++); // count = 1 again
        monitor.OnError(terminalError, _ => closeCallCount++); // count = 2

        Assert.Equal(0, closeCallCount); // never reached 3
    }

    // --- OnClose ---

    [Fact]
    public void OnClose_NotifiesSink()
    {
        var monitor = BuildMonitor("sse");
        monitor.OnClose();

        Assert.NotNull(_sink.LastClosedServerName);
        Assert.Equal("server1", _sink.LastClosedServerName);
    }

    // Sink capture helper

    private sealed class CapturingSink : IMcpConnectionEventSink
    {
        public string? LastSessionExpiredServer { get; private set; }
        public string? LastClosedServerName { get; private set; }
        public string? LastTerminalErrorServer { get; private set; }
        public McpConnectionError? LastConnectionError { get; private set; }

        public void OnSessionExpired(string serverName) => LastSessionExpiredServer = serverName;

        public void OnConnectionClosed(string serverName, TimeSpan uptime, bool hadErrors)
            => LastClosedServerName = serverName;

        public void OnTerminalErrorThreshold(string serverName, string reason)
            => LastTerminalErrorServer = serverName;

        public void OnConnectionError(string serverName, string transportType, McpConnectionError error)
            => LastConnectionError = error;
    }
}
