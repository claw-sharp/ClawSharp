using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class RemoteSessionHeartbeatRuntimeTests
{
    [Fact]
    public async Task Start_Heartbeats_Current_Work_Item_Using_Configured_Jitter()
    {
        var api = new StubBridgeApiClient();
        var state = CreateState();
        var delays = new List<double>();
        var heartbeatSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sleepCalls = 0;

        await using var runtime = new RemoteSessionHeartbeatRuntime(
            new RemoteSessionHeartbeatRuntimeDependencies(
                new BridgeHeartbeatDependencies(api, new StubBridgeLogger()),
                state,
                new EnvLessBridgeConfig(
                    InitRetryMaxAttempts: 3,
                    InitRetryBaseDelayMs: 500,
                    InitRetryJitterFraction: 0.25,
                    InitRetryMaxDelayMs: 4_000,
                    HttpTimeoutMs: 10_000,
                    UuidDedupBufferSize: 2_000,
                    HeartbeatIntervalMs: 20_000,
                    HeartbeatJitterFraction: 0.1,
                    TokenRefreshBufferMs: 300_000,
                    TeardownArchiveTimeoutMs: 1_500,
                    ConnectTimeoutMs: 15_000,
                    MinVersion: "0.0.0",
                    ShouldShowAppUpgradeMessage: false),
                SleepAsync: (delayMs, token) =>
                {
                    delays.Add(delayMs);
                    sleepCalls++;
                    return sleepCalls == 1
                        ? Task.CompletedTask
                        : Task.Delay(Timeout.InfiniteTimeSpan, token);
                },
                NextRandomDouble: () => 1d));

        api.OnHeartbeat = () => heartbeatSeen.TrySetResult();
        runtime.Start();

        await heartbeatSeen.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal([22_000d, 22_000d], delays);
        Assert.Equal([("env_123", "work_123", "token_123")], api.HeartbeatCalls);
    }

    [Fact]
    public async Task Start_Uses_Updated_Reconnect_State_On_Later_Heartbeats()
    {
        var api = new StubBridgeApiClient();
        var state = CreateState();
        var heartbeatSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sleepCalls = 0;

        await using var runtime = new RemoteSessionHeartbeatRuntime(
            new RemoteSessionHeartbeatRuntimeDependencies(
                new BridgeHeartbeatDependencies(api, new StubBridgeLogger()),
                state,
                EnvLessBridgeConfig.Default,
                SleepAsync: (_, token) =>
                {
                    sleepCalls++;
                    return sleepCalls <= 2
                        ? Task.CompletedTask
                        : Task.Delay(Timeout.InfiniteTimeSpan, token);
                },
                NextRandomDouble: () => 0.5d));

        api.OnHeartbeat = () =>
        {
            if (api.HeartbeatCalls.Count == 1)
            {
                state.EnvironmentId = "env_456";
                state.CurrentWorkId = "work_456";
                state.CurrentIngressToken = "token_456";
            }

            if (api.HeartbeatCalls.Count == 2)
            {
                heartbeatSeen.TrySetResult();
            }
        };

        runtime.Start();

        await heartbeatSeen.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(
            [("env_123", "work_123", "token_123"), ("env_456", "work_456", "token_456")],
            api.HeartbeatCalls);
    }

    [Fact]
    public async Task Start_Skips_Heartbeat_When_Current_Work_Is_Not_Known()
    {
        var api = new StubBridgeApiClient();
        var state = CreateState();
        state.CurrentWorkId = null;
        var firstSleep = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sleepCalls = 0;

        await using var runtime = new RemoteSessionHeartbeatRuntime(
            new RemoteSessionHeartbeatRuntimeDependencies(
                new BridgeHeartbeatDependencies(api, new StubBridgeLogger()),
                state,
                EnvLessBridgeConfig.Default,
                SleepAsync: (_, token) =>
                {
                    sleepCalls++;
                    if (sleepCalls == 1)
                    {
                        firstSleep.TrySetResult();
                        return Task.CompletedTask;
                    }

                    return Task.Delay(Timeout.InfiniteTimeSpan, token);
                },
                NextRandomDouble: () => 0.5d));

        runtime.Start();
        await firstSleep.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await Task.Delay(50);

        Assert.Empty(api.HeartbeatCalls);
    }

    private static BridgeTransportReconnectState CreateState()
    {
        return new BridgeTransportReconnectState
        {
            BridgeConfig = new BridgeConfig(
                Dir: "D:\\repo",
                MachineName: "machine",
                Branch: "main",
                GitRepoUrl: "https://example.com/repo.git",
                MaxSessions: 1,
                SpawnMode: SpawnMode.SingleSession,
                Verbose: false,
                Sandbox: false,
                BridgeId: "bridge_123",
                WorkerType: "claude-code",
                EnvironmentId: "env_123",
                ApiBaseUrl: "https://example.com",
                SessionIngressUrl: "wss://example.com"),
            EnvironmentId = "env_123",
            EnvironmentSecret = "secret_1",
            CurrentSessionId = "session_123",
            CurrentWorkId = "work_123",
            CurrentIngressToken = "token_123"
        };
    }

    private sealed class StubBridgeApiClient : IBridgeApiClient
    {
        public List<(string EnvironmentId, string WorkId, string SessionToken)> HeartbeatCalls { get; } = [];
        public Action? OnHeartbeat { get; set; }

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
            throw new NotImplementedException();
        }

        public Task<BridgeHeartbeatResponse> HeartbeatWorkAsync(string environmentId, string workId, string sessionToken, CancellationToken cancellationToken = default)
        {
            HeartbeatCalls.Add((environmentId, workId, sessionToken));
            OnHeartbeat?.Invoke();
            return Task.FromResult(new BridgeHeartbeatResponse(true, "running"));
        }
    }

    private sealed class StubBridgeLogger : IBridgeLogger
    {
        public void PrintBanner(BridgeConfig config, string environmentId) => throw new NotImplementedException();
        public void LogSessionStart(string sessionId, string prompt) => throw new NotImplementedException();
        public void LogSessionComplete(string sessionId, long durationMs) => throw new NotImplementedException();
        public void LogSessionFailed(string sessionId, string error) => throw new NotImplementedException();
        public void LogStatus(string message) => throw new NotImplementedException();
        public void LogVerbose(string message) { }
        public void LogError(string message) => throw new NotImplementedException();
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
