using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgeHeartbeatCoordinatorTests
{
    [Fact]
    public async Task HeartbeatActiveWorkItemsAsync_Requeues_AuthFailed_Sessions_And_Returns_AuthFailed()
    {
        var api = new StubBridgeApiClient
        {
            HeartbeatErrors =
            [
                new BridgeFatalError("expired", 401, "auth_failed"),
                null
            ]
        };
        var logger = new StubBridgeLogger();
        var debugMessages = new List<string>();

        var result = await BridgeHeartbeatCoordinator.HeartbeatActiveWorkItemsAsync(
            new BridgeHeartbeatDependencies(api, logger, debugMessages.Add),
            "env_123",
            [
                new BridgeHeartbeatWorkItem("session_123", "work_123", "token_123"),
                new BridgeHeartbeatWorkItem("session_456", "work_456", "token_456")
            ]);

        Assert.Equal(BridgeHeartbeatResult.AuthFailed, result);
        Assert.Equal(["session_123"], api.ReconnectAttempts);
        Assert.Contains(logger.VerboseMessages, message => message.Contains("token expired", StringComparison.Ordinal));
        Assert.Contains(debugMessages, message => message.Contains("Re-queued sessionId=session_123", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HeartbeatActiveWorkItemsAsync_Returns_Fatal_For_NonAuth_Fatal_Error()
    {
        var api = new StubBridgeApiClient
        {
            HeartbeatErrors =
            [
                new BridgeFatalError("gone", 404, "environment_expired")
            ]
        };

        var result = await BridgeHeartbeatCoordinator.HeartbeatActiveWorkItemsAsync(
            new BridgeHeartbeatDependencies(api, new StubBridgeLogger()),
            "env_123",
            [
                new BridgeHeartbeatWorkItem("session_123", "work_123", "token_123")
            ]);

        Assert.Equal(BridgeHeartbeatResult.Fatal, result);
        Assert.Empty(api.ReconnectAttempts);
    }

    [Fact]
    public async Task HeartbeatActiveWorkItemsAsync_Returns_Failed_When_All_Heartbeats_Fail_NonFatally()
    {
        var api = new StubBridgeApiClient
        {
            HeartbeatErrors =
            [
                new InvalidOperationException("timeout")
            ]
        };

        var result = await BridgeHeartbeatCoordinator.HeartbeatActiveWorkItemsAsync(
            new BridgeHeartbeatDependencies(api, new StubBridgeLogger()),
            "env_123",
            [
                new BridgeHeartbeatWorkItem("session_123", "work_123", "token_123")
            ]);

        Assert.Equal(BridgeHeartbeatResult.Failed, result);
    }

    [Fact]
    public async Task TryHeartbeatCurrentWorkItemAsync_Swallows_Failures()
    {
        var api = new StubBridgeApiClient
        {
            HeartbeatErrors =
            [
                new InvalidOperationException("unavailable")
            ]
        };

        await BridgeHeartbeatCoordinator.TryHeartbeatCurrentWorkItemAsync(
            new BridgeHeartbeatDependencies(api, new StubBridgeLogger()),
            new BridgeHeartbeatInfo("env_123", "work_123", "token_123"));

        Assert.Equal(1, api.HeartbeatCalls);
    }

    private sealed class StubBridgeApiClient : IBridgeApiClient
    {
        public List<string> ReconnectAttempts { get; } = [];
        public List<Exception?> HeartbeatErrors { get; init; } = [];
        public int HeartbeatCalls { get; private set; }

        public Task<BridgeRegisterEnvironmentResponse> RegisterBridgeEnvironmentAsync(BridgeConfig config, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<BridgeWorkResponse?> PollForWorkAsync(string environmentId, string environmentSecret, CancellationToken cancellationToken = default, int? reclaimOlderThanMs = null)
        {
            throw new NotImplementedException();
        }

        public Task AcknowledgeWorkAsync(string environmentId, string workId, string sessionToken, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task StopWorkAsync(string environmentId, string workId, bool force, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task DeregisterEnvironmentAsync(string environmentId, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task SendPermissionResponseEventAsync(string sessionId, PermissionResponseEvent @event, string sessionToken, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task ArchiveSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task ReconnectSessionAsync(string environmentId, string sessionId, CancellationToken cancellationToken = default)
        {
            ReconnectAttempts.Add(sessionId);
            return Task.CompletedTask;
        }

        public Task<BridgeHeartbeatResponse> HeartbeatWorkAsync(string environmentId, string workId, string sessionToken, CancellationToken cancellationToken = default)
        {
            var callIndex = HeartbeatCalls++;
            if (callIndex < HeartbeatErrors.Count && HeartbeatErrors[callIndex] is Exception error)
            {
                throw error;
            }

            return Task.FromResult(new BridgeHeartbeatResponse(true, "running"));
        }
    }

    private sealed class StubBridgeLogger : IBridgeLogger
    {
        public List<string> VerboseMessages { get; } = [];
        public List<string> ErrorMessages { get; } = [];

        public void PrintBanner(BridgeConfig config, string environmentId) => throw new NotImplementedException();
        public void LogSessionStart(string sessionId, string prompt) => throw new NotImplementedException();
        public void LogSessionComplete(string sessionId, long durationMs) => throw new NotImplementedException();
        public void LogSessionFailed(string sessionId, string error) => throw new NotImplementedException();
        public void LogStatus(string message) => throw new NotImplementedException();
        public void LogVerbose(string message) => VerboseMessages.Add(message);
        public void LogError(string message) => ErrorMessages.Add(message);
        public void LogReconnected(long disconnectedMs) => throw new NotImplementedException();
        public void UpdateIdleStatus() => throw new NotImplementedException();
        public void UpdateReconnectingStatus(string delay, string elapsed) => throw new NotImplementedException();
        public void UpdateSessionStatus(string sessionId, string elapsed, SessionActivity activity, IReadOnlyList<string> trail) => throw new NotImplementedException();
        public void ClearStatus() => throw new NotImplementedException();
        public void SetRepoInfo(string repoName, string branch) => throw new NotImplementedException();
        public void SetDebugLogPath(string path) => throw new NotImplementedException();
        public void SetAttached(string sessionId) => throw new NotImplementedException();
        public void UpdateFailedStatus(string error) => throw new NotImplementedException();
        public void ToggleQr() => throw new NotImplementedException();
        public void UpdateSessionCount(int active, int max, SpawnMode mode) => throw new NotImplementedException();
        public void SetSpawnModeDisplay(SpawnMode? mode) => throw new NotImplementedException();
        public void AddSession(string sessionId, string url) => throw new NotImplementedException();
        public void UpdateSessionActivity(string sessionId, SessionActivity activity) => throw new NotImplementedException();
        public void SetSessionTitle(string sessionId, string title) => throw new NotImplementedException();
        public void RemoveSession(string sessionId) => throw new NotImplementedException();
        public void RefreshDisplay() => throw new NotImplementedException();
    }
}
