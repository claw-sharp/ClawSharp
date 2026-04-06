// TS origin: ./bridge/bridgeApi.ts, ./bridge/bridgeDebug.ts
using System.Text.Json.Nodes;
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

[Collection("TelemetrySerial")]
public sealed class BridgeApiErrorUtilitiesTests
{
    [Theory]
    [InlineData("environment_expired", true)]
    [InlineData("session_lifetime_exceeded", true)]
    [InlineData("permission_denied", false)]
    [InlineData(null, false)]
    public void IsExpiredErrorType_Matches_Ts_String_Check(string? errorType, bool expected)
    {
        Assert.Equal(expected, BridgeApiErrorUtilities.IsExpiredErrorType(errorType));
    }

    [Fact]
    public void IsSuppressible403_Matches_Known_Scope_And_Manage_Errors()
    {
        Assert.True(BridgeApiErrorUtilities.IsSuppressible403(
            new BridgeFatalError("missing external_poll_sessions scope", 403)));
        Assert.True(BridgeApiErrorUtilities.IsSuppressible403(
            new BridgeFatalError("requires environments:manage", 403)));
        Assert.False(BridgeApiErrorUtilities.IsSuppressible403(
            new BridgeFatalError("requires environments:manage", 404)));
    }

    [Theory]
    [InlineData("session_abc123", "sessionId")]
    [InlineData("cse_staging_abc123", "environmentId")]
    public void ValidateBridgeId_Allows_Safe_Tagged_Ids(string id, string label)
    {
        Assert.Equal(id, BridgeApiErrorUtilities.ValidateBridgeId(id, label));
    }

    [Theory]
    [InlineData("", "sessionId")]
    [InlineData("../escape", "sessionId")]
    [InlineData("abc/def", "environmentId")]
    [InlineData("abc.def", "workId")]
    public void ValidateBridgeId_Rejects_Unsafe_Ids(string id, string label)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            BridgeApiErrorUtilities.ValidateBridgeId(id, label));

        Assert.Equal($"Invalid {label}: contains unsafe characters", error.Message);
    }

    [Fact]
    public void ExtractErrorTypeFromData_Reads_Nested_Error_Type()
    {
        var data = JsonNode.Parse("""{"error":{"type":"environment_expired"}}""");

        var errorType = BridgeApiErrorUtilities.ExtractErrorTypeFromData(data);

        Assert.Equal("environment_expired", errorType);
    }

    [Fact]
    public void HandleErrorStatus_Maps_Fatal_And_Nonfatal_Statuses_Like_Ts()
    {
        var unauthorized = Assert.Throws<BridgeFatalError>(() =>
            BridgeApiErrorUtilities.HandleErrorStatus(401, JsonNode.Parse("""{"message":"bad token","error":{"type":"auth_error"}}"""), "Registration"));
        var forbiddenExpired = Assert.Throws<BridgeFatalError>(() =>
            BridgeApiErrorUtilities.HandleErrorStatus(403, JsonNode.Parse("""{"error":{"type":"session_expired"}}"""), "ReconnectSession"));
        var notFound = Assert.Throws<BridgeFatalError>(() =>
            BridgeApiErrorUtilities.HandleErrorStatus(404, null, "Poll"));
        var gone = Assert.Throws<BridgeFatalError>(() =>
            BridgeApiErrorUtilities.HandleErrorStatus(410, JsonNode.Parse("""{"message":"expired"}"""), "Heartbeat"));
        var rateLimited = Assert.Throws<InvalidOperationException>(() =>
            BridgeApiErrorUtilities.HandleErrorStatus(429, null, "Poll"));

        Assert.Equal(401, unauthorized.Status);
        Assert.Equal("auth_error", unauthorized.ErrorType);
        Assert.Contains("Authentication failed (401): bad token.", unauthorized.Message);
        Assert.Contains(BridgeConstants.BridgeLoginInstruction, unauthorized.Message);
        Assert.Equal(403, forbiddenExpired.Status);
        Assert.Equal("session_expired", forbiddenExpired.ErrorType);
        Assert.Equal(
            "Remote Control session has expired. Please restart with `clawsharp remote-control` or /remote-control.",
            forbiddenExpired.Message);
        Assert.Equal(
            "Poll: Not found (404). Remote Control may not be available for this organization.",
            notFound.Message);
        Assert.Equal(410, gone.Status);
        Assert.Equal("environment_expired", gone.ErrorType);
        Assert.Equal("expired", gone.Message);
        Assert.Equal("Poll: Rate limited (429). Polling too frequently.", rateLimited.Message);
    }

    [Fact]
    public async Task WrapApiForFaultInjection_Throws_Fatal_And_Transient_Injected_Faults()
    {
        BridgeDebugState.ClearBridgeDebugHandle();
        var api = new StubBridgeApiClient();
        var wrapped = BridgeDebugState.WrapApiForFaultInjection(api);

        BridgeDebugState.InjectBridgeFault(new BridgeFault("pollForWork", "fatal", 404, "not_found_error"));
        BridgeDebugState.InjectBridgeFault(new BridgeFault("heartbeatWork", "transient", 503));

        var fatal = await Assert.ThrowsAsync<BridgeFatalError>(() =>
            wrapped.PollForWorkAsync("env", "secret"));
        var transient = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            wrapped.HeartbeatWorkAsync("env", "work", "token"));

        Assert.Equal(404, fatal.Status);
        Assert.Equal("not_found_error", fatal.ErrorType);
        Assert.Equal("[injected transient] Heartbeat 503", transient.Message);
    }

    [Fact]
    public async Task WrapApiForFaultInjection_Delegates_When_No_Fault_Is_Queued()
    {
        BridgeDebugState.ClearBridgeDebugHandle();
        var api = new StubBridgeApiClient();
        var wrapped = BridgeDebugState.WrapApiForFaultInjection(api);

        var response = await wrapped.PollForWorkAsync("env", "secret");

        Assert.Null(response);
        Assert.True(api.PollCalled);
    }

    private sealed class StubBridgeApiClient : IBridgeApiClient
    {
        public bool PollCalled { get; private set; }

        public Task<BridgeRegisterEnvironmentResponse> RegisterBridgeEnvironmentAsync(BridgeConfig config, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new BridgeRegisterEnvironmentResponse("env", "secret"));
        }

        public Task<BridgeWorkResponse?> PollForWorkAsync(string environmentId, string environmentSecret, CancellationToken cancellationToken = default, int? reclaimOlderThanMs = null)
        {
            PollCalled = true;
            return Task.FromResult<BridgeWorkResponse?>(null);
        }

        public Task AcknowledgeWorkAsync(string environmentId, string workId, string sessionToken, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task StopWorkAsync(string environmentId, string workId, bool force, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeregisterEnvironmentAsync(string environmentId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task SendPermissionResponseEventAsync(string sessionId, PermissionResponseEvent @event, string sessionToken, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ArchiveSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ReconnectSessionAsync(string environmentId, string sessionId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<BridgeHeartbeatResponse> HeartbeatWorkAsync(string environmentId, string workId, string sessionToken, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new BridgeHeartbeatResponse(true, "running"));
        }
    }
}
