// TS origin: ./bridge/bridgeMain.ts
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgeRetryUtilitiesTests
{
    [Fact]
    public void AddJitter_And_FormatDelay_Match_Ts_Helper_Behavior()
    {
        Assert.Equal(75d, BridgeRetryUtilities.AddJitter(100d, 0d));
        Assert.Equal(100d, BridgeRetryUtilities.AddJitter(100d, 0.5d));
        Assert.Equal(125d, BridgeRetryUtilities.AddJitter(100d, 1d));
        Assert.Equal("850ms", BridgeRetryUtilities.FormatDelay(850.4d));
        Assert.Equal("1.3s", BridgeRetryUtilities.FormatDelay(1250d));
    }

    [Fact]
    public async Task StopWorkWithRetryAsync_Retries_Transient_Failures_With_Backoff()
    {
        var logger = new TestBridgeLogger();
        var api = new TestBridgeApiClient();
        api.StopWorkImpl = (_, _, _, _) =>
        {
            api.Attempts++;
            if (api.Attempts < 3)
            {
                throw new InvalidOperationException("transient");
            }

            return Task.CompletedTask;
        };
        List<double> slept = [];

        await BridgeRetryUtilities.StopWorkWithRetryAsync(
            new BridgeStopWorkRetryDependencies(
                api,
                logger,
                SleepAsync: delay =>
                {
                    slept.Add(delay);
                    return Task.CompletedTask;
                },
                NextRandomDouble: () => 0.5d),
            "env",
            "work");

        Assert.Equal(3, api.Attempts);
        Assert.Equal([1000d, 2000d], slept);
        Assert.Equal(
            "Failed to stop work work (attempt 1/3), retrying in 1.0s: transient",
            logger.VerboseMessages[0]);
        Assert.Equal(
            "Failed to stop work work (attempt 2/3), retrying in 2.0s: transient",
            logger.VerboseMessages[1]);
        Assert.Empty(logger.ErrorMessages);
    }

    [Fact]
    public async Task StopWorkWithRetryAsync_Suppresses_Fatal_403_And_Stops_Retrying()
    {
        var logger = new TestBridgeLogger();
        var api = new TestBridgeApiClient();
        api.StopWorkImpl = (_, _, _, _) =>
        {
            api.Attempts++;
            throw new BridgeFatalError("external_poll_sessions denied", 403);
        };
        List<(int Attempts, bool? Fatal)> diagnostics = [];
        List<string> debug = [];

        await BridgeRetryUtilities.StopWorkWithRetryAsync(
            new BridgeStopWorkRetryDependencies(
                api,
                logger,
                debug.Add,
                (attempts, fatal) => diagnostics.Add((attempts, fatal))),
            "env",
            "work");

        Assert.Equal(1, api.Attempts);
        Assert.Empty(logger.ErrorMessages);
        Assert.Contains(debug, line => line.Contains("Suppressed stopWork 403", StringComparison.Ordinal));
        Assert.Equal([(1, (bool?)true)], diagnostics);
    }

    [Fact]
    public async Task StopWorkWithRetryAsync_Logs_Final_Error_After_Last_Transient_Failure()
    {
        var logger = new TestBridgeLogger();
        var api = new TestBridgeApiClient();
        api.StopWorkImpl = (_, _, _, _) =>
        {
            api.Attempts++;
            throw new InvalidOperationException("broken");
        };
        List<(int Attempts, bool? Fatal)> diagnostics = [];

        await BridgeRetryUtilities.StopWorkWithRetryAsync(
            new BridgeStopWorkRetryDependencies(
                api,
                logger,
                OnStopWorkFailed: (attempts, fatal) => diagnostics.Add((attempts, fatal)),
                SleepAsync: _ => Task.CompletedTask,
                NextRandomDouble: () => 0.5d),
            "env",
            "work");

        Assert.Equal(3, api.Attempts);
        Assert.Equal("Failed to stop work work after 3 attempts: broken", logger.ErrorMessages.Single());
        Assert.Equal([(3, (bool?)null)], diagnostics);
    }

    private sealed class TestBridgeApiClient : IBridgeApiClient
    {
        public int Attempts { get; set; }

        public Func<string, string, bool, CancellationToken, Task>? StopWorkImpl { get; set; }

        public Task<BridgeRegisterEnvironmentResponse> RegisterBridgeEnvironmentAsync(BridgeConfig config, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BridgeWorkResponse?> PollForWorkAsync(string environmentId, string environmentSecret, CancellationToken cancellationToken = default, int? reclaimOlderThanMs = null)
            => throw new NotSupportedException();

        public Task AcknowledgeWorkAsync(string environmentId, string workId, string sessionToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task StopWorkAsync(string environmentId, string workId, bool force, CancellationToken cancellationToken = default)
            => StopWorkImpl!(environmentId, workId, force, cancellationToken);

        public Task DeregisterEnvironmentAsync(string environmentId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SendPermissionResponseEventAsync(string sessionId, PermissionResponseEvent @event, string sessionToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ArchiveSessionAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ReconnectSessionAsync(string environmentId, string sessionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BridgeHeartbeatResponse> HeartbeatWorkAsync(string environmentId, string workId, string sessionToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class TestBridgeLogger : IBridgeLogger
    {
        public List<string> VerboseMessages { get; } = [];

        public List<string> ErrorMessages { get; } = [];

        public void PrintBanner(BridgeConfig config, string environmentId) => throw new NotSupportedException();
        public void LogSessionStart(string sessionId, string prompt) => throw new NotSupportedException();
        public void LogSessionComplete(string sessionId, long durationMs) => throw new NotSupportedException();
        public void LogSessionFailed(string sessionId, string error) => throw new NotSupportedException();
        public void LogStatus(string message) => throw new NotSupportedException();
        public void LogVerbose(string message) => VerboseMessages.Add(message);
        public void LogError(string message) => ErrorMessages.Add(message);
        public void LogReconnected(long disconnectedMs) => throw new NotSupportedException();
        public void UpdateIdleStatus() => throw new NotSupportedException();
        public void UpdateReconnectingStatus(string delay, string elapsed) => throw new NotSupportedException();
        public void UpdateSessionStatus(string sessionId, string elapsed, SessionActivity activity, IReadOnlyList<string> trail) => throw new NotSupportedException();
        public void ClearStatus() => throw new NotSupportedException();
        public void SetRepoInfo(string repoName, string branch) => throw new NotSupportedException();
        public void SetDebugLogPath(string path) => throw new NotSupportedException();
        public void SetAttached(string sessionId) => throw new NotSupportedException();
        public void UpdateFailedStatus(string error) => throw new NotSupportedException();
        public void ToggleQr() => throw new NotSupportedException();
        public void UpdateSessionCount(int active, int max, SpawnMode mode) => throw new NotSupportedException();
        public void SetSpawnModeDisplay(SpawnMode? mode) => throw new NotSupportedException();
        public void AddSession(string sessionId, string url) => throw new NotSupportedException();
        public void UpdateSessionActivity(string sessionId, SessionActivity activity) => throw new NotSupportedException();
        public void SetSessionTitle(string sessionId, string title) => throw new NotSupportedException();
        public void RemoveSession(string sessionId) => throw new NotSupportedException();
        public void RefreshDisplay() => throw new NotSupportedException();
    }
}
