using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgePollLoopRunnerTests
{
    [Fact]
    public async Task RunAsync_WhenNoWorkAndBelowCapacity_Sleeps_NotAtCapacity_Interval()
    {
        using var cts = new CancellationTokenSource();
        var sleepCalls = new List<double>();
        var api = new StubBridgeApiClient
        {
            PollResponses = new Queue<BridgeWorkResponse?>([null])
        };
        var dependencies = CreateDependencies(
            api,
            getActiveSessionCount: () => 0,
            sleepAsync: (delay, cancellationToken) =>
            {
                sleepCalls.Add(delay);
                cts.Cancel();
                return Task.CompletedTask;
            });

        var result = await BridgePollLoopRunner.RunAsync(
            dependencies,
            "env_123",
            "secret",
            maxSessions: 2,
            cancellationToken: cts.Token);

        Assert.False(result.FatalExit);
        Assert.Equal([BridgePollConfig.Default.MultisessionPollIntervalMsNotAtCapacity], sleepCalls);
    }

    [Fact]
    public async Task RunAsync_WhenNoWorkAndAtCapacity_Uses_Heartbeat_Mode_Until_PollDue()
    {
        using var cts = new CancellationTokenSource();
        var sleepCalls = new List<double>();
        var heartbeatCalls = 0;
        var heartbeatModeResults = new List<BridgePollLoopHeartbeatModeResult>();
        var pollCount = 0;
        var api = new StubBridgeApiClient
        {
            PollFunc = (_, _, _, _) =>
            {
                pollCount++;
                if (pollCount == 1)
                {
                    return Task.FromResult<BridgeWorkResponse?>(null);
                }

                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            }
        };
        var now = new Queue<DateTimeOffset>(
        [
            new DateTimeOffset(2026, 4, 2, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 2, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 2, 12, 0, 1, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 2, 12, 0, 2, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 2, 12, 0, 2, TimeSpan.Zero)
        ]);
        var dependencies = CreateDependencies(
            api,
            getActiveSessionCount: () => 2,
            getPollConfig: () => BridgePollConfig.Default with
            {
                NonExclusiveHeartbeatIntervalMs = 1_000,
                MultisessionPollIntervalMsAtCapacity = 2_000
            },
            heartbeatAsync: _ =>
            {
                heartbeatCalls++;
                return Task.FromResult(BridgeHeartbeatResult.Ok);
            },
            sleepAsync: (delay, cancellationToken) =>
            {
                sleepCalls.Add(delay);
                return Task.CompletedTask;
            },
            getNow: () => now.Dequeue(),
            onHeartbeatModeExited: heartbeatModeResults.Add);

        var result = await BridgePollLoopRunner.RunAsync(
            dependencies,
            "env_123",
            "secret",
            maxSessions: 2,
            cancellationToken: cts.Token);

        Assert.False(result.FatalExit);
        Assert.Equal(2, heartbeatCalls);
        Assert.Equal([1000d, 1000d], sleepCalls);
        Assert.Single(heartbeatModeResults);
        Assert.Equal(BridgeHeartbeatModeExitReason.PollDue, heartbeatModeResults[0].ExitReason);
        Assert.Equal(2, heartbeatModeResults[0].HeartbeatCycles);
        Assert.Equal(2, pollCount);
    }

    [Fact]
    public async Task RunAsync_WhenAtCapacityAfterWork_HeartbeatsAndSleeps()
    {
        using var cts = new CancellationTokenSource();
        var sleepCalls = new List<double>();
        var heartbeatCalls = 0;
        var onWorkCalls = 0;
        var api = new StubBridgeApiClient
        {
            PollResponses = new Queue<BridgeWorkResponse?>(
            [
                new BridgeWorkResponse(
                    "work_123",
                    "work",
                    "env_123",
                    "running",
                    new BridgeWorkData(BridgeWorkDataType.Session, "session_123"),
                    "secret",
                    "2026-04-02T12:00:00Z")
            ])
        };
        var dependencies = CreateDependencies(
            api,
            getActiveSessionCount: () => 2,
            heartbeatAsync: _ =>
            {
                heartbeatCalls++;
                return Task.FromResult(BridgeHeartbeatResult.Ok);
            },
            onWorkAsync: (_, _, _, _) =>
            {
                onWorkCalls++;
                return Task.CompletedTask;
            },
            getPollConfig: () => BridgePollConfig.Default with
            {
                NonExclusiveHeartbeatIntervalMs = 1_500
            },
            sleepAsync: (delay, cancellationToken) =>
            {
                sleepCalls.Add(delay);
                cts.Cancel();
                return Task.CompletedTask;
            });

        var result = await BridgePollLoopRunner.RunAsync(
            dependencies,
            "env_123",
            "secret",
            maxSessions: 2,
            cancellationToken: cts.Token);

        Assert.False(result.FatalExit);
        Assert.Equal(1, onWorkCalls);
        Assert.Equal(1, heartbeatCalls);
        Assert.Equal([1500d], sleepCalls);
    }

    [Fact]
    public async Task RunAsync_OnConnectionError_Applies_Connection_Backoff_And_Reconnecting_Status()
    {
        using var cts = new CancellationTokenSource();
        var sleepCalls = new List<double>();
        var logger = new StubBridgeLogger();
        var api = new StubBridgeApiClient
        {
            PollException = new InvalidOperationException("ECONNREFUSED")
        };
        var dependencies = CreateDependencies(
            api,
            logger: logger,
            isConnectionError: error => error.Message.Contains("ECONNREFUSED", StringComparison.Ordinal),
            sleepAsync: (delay, cancellationToken) =>
            {
                sleepCalls.Add(delay);
                cts.Cancel();
                return Task.CompletedTask;
            },
            getPollConfig: () => BridgePollConfig.Default with
            {
                NonExclusiveHeartbeatIntervalMs = 0
            });

        var result = await BridgePollLoopRunner.RunAsync(
            dependencies,
            "env_123",
            "secret",
            maxSessions: 2,
            cancellationToken: cts.Token,
            backoffConfig: BridgeBackoffConfig.Default with
            {
                ConnInitialMs = 2_000,
                ConnCapMs = 120_000
            });

        Assert.False(result.FatalExit);
        Assert.Single(sleepCalls);
        Assert.InRange(sleepCalls[0], 1500d, 2500d);
        Assert.Contains(logger.VerboseMessages, message => message.Contains("Connection error, retrying in", StringComparison.Ordinal));
        Assert.NotNull(logger.ReconnectingStatus);
    }

    [Fact]
    public async Task RunAsync_OnFatalBridgeError_Stops_With_Fatal_Result()
    {
        var logger = new StubBridgeLogger();
        var fatal = new BridgeFatalError("boom", 404, "environment_expired");
        var api = new StubBridgeApiClient
        {
            PollException = fatal
        };
        var dependencies = CreateDependencies(api, logger: logger);

        var result = await BridgePollLoopRunner.RunAsync(
            dependencies,
            "env_123",
            "secret",
            maxSessions: 2,
            cancellationToken: CancellationToken.None);

        Assert.True(result.FatalExit);
        Assert.Same(fatal, result.FatalError);
        Assert.Contains("boom", logger.StatusMessages.Single());
    }

    private static BridgePollLoopDependencies CreateDependencies(
        StubBridgeApiClient api,
        StubBridgeLogger? logger = null,
        Func<int>? getActiveSessionCount = null,
        Func<BridgePollConfig>? getPollConfig = null,
        Func<CancellationToken, Task<BridgeHeartbeatResult>>? heartbeatAsync = null,
        Func<BridgeWorkResponse, bool, BridgePollConfig, CancellationToken, Task>? onWorkAsync = null,
        Func<double, CancellationToken, Task>? sleepAsync = null,
        Func<DateTimeOffset>? getNow = null,
        Action<BridgePollLoopHeartbeatModeResult>? onHeartbeatModeExited = null,
        Func<Exception, bool>? isConnectionError = null)
    {
        return new BridgePollLoopDependencies(
            Api: api,
            Logger: logger ?? new StubBridgeLogger(),
            CapacityWake: new CapacityWake(CancellationToken.None),
            GetPollConfig: getPollConfig ?? (() => BridgePollConfig.Default),
            GetActiveSessionCount: getActiveSessionCount ?? (() => 0),
            OnWorkAsync: onWorkAsync ?? ((_, _, _, _) => Task.CompletedTask),
            HeartbeatActiveWorkItemsAsync: heartbeatAsync ?? (_ => Task.FromResult(BridgeHeartbeatResult.Ok)),
            SleepAsync: sleepAsync,
            GetNow: getNow,
            OnHeartbeatModeExited: onHeartbeatModeExited,
            IsConnectionError: isConnectionError,
            DescribeError: static error => error.Message);
    }

    private sealed class StubBridgeApiClient : IBridgeApiClient
    {
        public Queue<BridgeWorkResponse?> PollResponses { get; init; } = [];
        public Exception? PollException { get; init; }
        public Func<string, string, CancellationToken, int?, Task<BridgeWorkResponse?>>? PollFunc { get; init; }

        public Task<BridgeRegisterEnvironmentResponse> RegisterBridgeEnvironmentAsync(BridgeConfig config, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<BridgeWorkResponse?> PollForWorkAsync(string environmentId, string environmentSecret, CancellationToken cancellationToken = default, int? reclaimOlderThanMs = null)
        {
            if (PollFunc is not null)
            {
                return PollFunc(environmentId, environmentSecret, cancellationToken, reclaimOlderThanMs);
            }

            if (PollException is not null)
            {
                throw PollException;
            }

            return Task.FromResult(PollResponses.Count > 0 ? PollResponses.Dequeue() : null);
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
            throw new NotImplementedException();
        }
    }

    private sealed class StubBridgeLogger : IBridgeLogger
    {
        public List<string> VerboseMessages { get; } = [];
        public List<string> StatusMessages { get; } = [];
        public (string Delay, string Elapsed)? ReconnectingStatus { get; private set; }

        public void PrintBanner(BridgeConfig config, string environmentId) => throw new NotImplementedException();
        public void LogSessionStart(string sessionId, string prompt) => throw new NotImplementedException();
        public void LogSessionComplete(string sessionId, long durationMs) => throw new NotImplementedException();
        public void LogSessionFailed(string sessionId, string error) => throw new NotImplementedException();
        public void LogStatus(string message) => StatusMessages.Add(message);
        public void LogVerbose(string message) => VerboseMessages.Add(message);
        public void LogError(string message) => StatusMessages.Add(message);
        public void LogReconnected(long disconnectedMs) => throw new NotImplementedException();
        public void UpdateIdleStatus() => throw new NotImplementedException();
        public void UpdateReconnectingStatus(string delay, string elapsed) => ReconnectingStatus = (delay, elapsed);
        public void UpdateSessionStatus(string sessionId, string elapsed, SessionActivity activity, IReadOnlyList<string> trail) => throw new NotImplementedException();
        public void ClearStatus() { }
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
