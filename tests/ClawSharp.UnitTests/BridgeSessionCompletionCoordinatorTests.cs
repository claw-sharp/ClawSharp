// TS origin: ./bridge/bridgeMain.ts
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgeSessionCompletionCoordinatorTests
{
    [Fact]
    public async Task CompleteAsync_TimedOutInterruptedSession_RemapsToFailed_AndSchedulesCleanup()
    {
        var api = new StubBridgeApiClient();
        var logger = new StubBridgeLogger();
        var wakeCount = 0;
        var tokenRefreshCancelled = new List<string>();
        var statusStarts = 0;
        var statusStops = 0;
        var removedWorktrees = new List<string>();
        var coordinator = new BridgeSessionCompletionCoordinator(new BridgeSessionCompletionDependencies(
            Api: api,
            Logger: logger,
            CapacityWake: new StubCapacityWake(() => wakeCount++),
            StopWorkRetryDependencies: new BridgeStopWorkRetryDependencies(api, logger),
            RemoveAgentWorktreeAsync: (worktree, _) =>
            {
                removedWorktrees.Add(worktree.WorktreePath);
                return Task.CompletedTask;
            },
            StopStatusUpdates: () => statusStops++,
            StartStatusUpdates: () => statusStarts++,
            CancelTokenRefresh: tokenRefreshCancelled.Add));

        var state = CreateState();
        var handle = new StubSessionHandle("session_123", ["stderr line"]);
        state.ActiveSessions["session_123"] = handle;
        state.SessionStartTimes["session_123"] = new DateTimeOffset(2026, 4, 2, 12, 0, 0, TimeSpan.Zero);
        state.SessionWorkIds["session_123"] = "work_123";
        state.SessionIngressTokens["session_123"] = "token";
        state.SessionCompatIds["session_123"] = "session_123";
        state.SessionWorktrees["session_123"] = new BridgeSessionWorktree("D:\\repo\\.wt\\session_123");
        state.TimedOutSessions.Add("session_123");
        state.TitledSessions.Add("session_123");
        state.V2Sessions.Add("session_123");

        var result = await coordinator.CompleteAsync(new BridgeSessionCompletionRequest(
            Config: CreateConfig(SpawnMode.SameDir),
            EnvironmentId: "env_123",
            SessionId: "session_123",
            StartTime: DateTimeOffset.UtcNow.AddSeconds(-10),
            Handle: handle,
            RawStatus: SessionDoneStatus.Interrupted,
            State: state,
            LoopAborted: false));

        await Task.WhenAll(state.PendingCleanups);

        Assert.Equal(SessionDoneStatus.Failed, result.FinalStatus);
        Assert.True(result.StopWorkScheduled);
        Assert.True(result.WorktreeCleanupScheduled);
        Assert.True(result.ArchiveScheduled);
        Assert.Equal(1, wakeCount);
        Assert.Equal(["session_123"], tokenRefreshCancelled);
        Assert.Equal(["D:\\repo\\.wt\\session_123"], removedWorktrees);
        Assert.Equal([("env_123", "work_123", false)], api.StopWorkCalls);
        Assert.Equal(["session_123"], api.ArchiveCalls);
        Assert.Equal(1, statusStops);
        Assert.Equal(1, statusStarts);
        Assert.Empty(state.ActiveSessions);
        Assert.Empty(state.SessionWorkIds);
        Assert.Empty(state.SessionIngressTokens);
        Assert.Empty(state.SessionCompatIds);
        Assert.Empty(state.SessionWorktrees);
        Assert.Empty(state.TimedOutSessions);
        Assert.Empty(state.TitledSessions);
        Assert.Empty(state.V2Sessions);
    }

    [Fact]
    public async Task CompleteAsync_SingleSessionCompletion_RequestsLoopAbort_InsteadOfArchive()
    {
        var api = new StubBridgeApiClient();
        var logger = new StubBridgeLogger();
        var aborted = new List<string>();
        var startedStatusUpdates = 0;
        var coordinator = new BridgeSessionCompletionCoordinator(new BridgeSessionCompletionDependencies(
            Api: api,
            Logger: logger,
            CapacityWake: new StubCapacityWake(() => { }),
            StopWorkRetryDependencies: new BridgeStopWorkRetryDependencies(api, logger),
            AbortLoop: aborted.Add,
            StartStatusUpdates: () => startedStatusUpdates++));

        var state = CreateState();
        var handle = new StubSessionHandle("session_123");
        state.ActiveSessions["session_123"] = handle;
        state.SessionStartTimes["session_123"] = DateTimeOffset.UtcNow.AddSeconds(-5);
        state.SessionWorkIds["session_123"] = "work_123";
        state.SessionCompatIds["session_123"] = "session_123";

        var result = await coordinator.CompleteAsync(new BridgeSessionCompletionRequest(
            Config: CreateConfig(SpawnMode.SingleSession),
            EnvironmentId: "env_123",
            SessionId: "session_123",
            StartTime: DateTimeOffset.UtcNow.AddSeconds(-5),
            Handle: handle,
            RawStatus: SessionDoneStatus.Completed,
            State: state,
            LoopAborted: false));

        await Task.WhenAll(state.PendingCleanups);

        Assert.True(result.RequestedLoopAbort);
        Assert.False(result.ArchiveScheduled);
        Assert.Equal(["session_123"], aborted);
        Assert.Equal(0, startedStatusUpdates);
        Assert.Equal([("env_123", "work_123", false)], api.StopWorkCalls);
        Assert.Empty(api.ArchiveCalls);
    }

    [Fact]
    public async Task CompleteAsync_InterruptedWhileLoopAlreadyAborted_SkipsStopWorkArchiveAndStatusRestart()
    {
        var api = new StubBridgeApiClient();
        var logger = new StubBridgeLogger();
        var startedStatusUpdates = 0;
        var coordinator = new BridgeSessionCompletionCoordinator(new BridgeSessionCompletionDependencies(
            Api: api,
            Logger: logger,
            CapacityWake: new StubCapacityWake(() => { }),
            StopWorkRetryDependencies: new BridgeStopWorkRetryDependencies(api, logger),
            StartStatusUpdates: () => startedStatusUpdates++));

        var state = CreateState();
        var handle = new StubSessionHandle("session_123");
        state.ActiveSessions["session_123"] = handle;
        state.SessionStartTimes["session_123"] = DateTimeOffset.UtcNow.AddSeconds(-5);
        state.SessionWorkIds["session_123"] = "work_123";
        state.SessionCompatIds["session_123"] = "session_123";

        var result = await coordinator.CompleteAsync(new BridgeSessionCompletionRequest(
            Config: CreateConfig(SpawnMode.SameDir),
            EnvironmentId: "env_123",
            SessionId: "session_123",
            StartTime: DateTimeOffset.UtcNow.AddSeconds(-5),
            Handle: handle,
            RawStatus: SessionDoneStatus.Interrupted,
            State: state,
            LoopAborted: true));

        Assert.Equal(SessionDoneStatus.Interrupted, result.FinalStatus);
        Assert.False(result.StopWorkScheduled);
        Assert.False(result.ArchiveScheduled);
        Assert.Equal(0, startedStatusUpdates);
        Assert.Empty(api.StopWorkCalls);
        Assert.Empty(api.ArchiveCalls);
    }

    private static BridgeSessionCompletionState CreateState()
    {
        return new BridgeSessionCompletionState(
            ActiveSessions: new Dictionary<string, ISessionHandle>(StringComparer.Ordinal),
            SessionStartTimes: new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal),
            SessionWorkIds: new Dictionary<string, string>(StringComparer.Ordinal),
            SessionIngressTokens: new Dictionary<string, string>(StringComparer.Ordinal),
            SessionCompatIds: new Dictionary<string, string>(StringComparer.Ordinal),
            SessionWorktrees: new Dictionary<string, BridgeSessionWorktree>(StringComparer.Ordinal),
            TimedOutSessions: new HashSet<string>(StringComparer.Ordinal),
            TitledSessions: new HashSet<string>(StringComparer.Ordinal),
            V2Sessions: new HashSet<string>(StringComparer.Ordinal),
            PendingCleanups: new HashSet<Task>());
    }

    private static BridgeConfig CreateConfig(SpawnMode spawnMode)
    {
        return new BridgeConfig(
            Dir: "D:\\repo",
            MachineName: "machine",
            Branch: "main",
            GitRepoUrl: "https://example.com/repo.git",
            MaxSessions: spawnMode == SpawnMode.SingleSession ? 1 : 32,
            SpawnMode: spawnMode,
            Verbose: false,
            Sandbox: false,
            BridgeId: "bridge_123",
            WorkerType: "claude_code",
            EnvironmentId: "env_client",
            ApiBaseUrl: "https://api.example.com",
            SessionIngressUrl: "https://ingress.example.com");
    }

    private sealed class StubBridgeApiClient : IBridgeApiClient
    {
        public List<(string EnvironmentId, string WorkId, bool Force)> StopWorkCalls { get; } = [];
        public List<string> ArchiveCalls { get; } = [];

        public Task<BridgeRegisterEnvironmentResponse> RegisterBridgeEnvironmentAsync(BridgeConfig config, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BridgeWorkResponse?> PollForWorkAsync(string environmentId, string environmentSecret, CancellationToken cancellationToken = default, int? reclaimOlderThanMs = null) => throw new NotSupportedException();
        public Task AcknowledgeWorkAsync(string environmentId, string workId, string sessionToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeregisterEnvironmentAsync(string environmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SendPermissionResponseEventAsync(string sessionId, PermissionResponseEvent @event, string sessionToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReconnectSessionAsync(string environmentId, string sessionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<BridgeHeartbeatResponse> HeartbeatWorkAsync(string environmentId, string workId, string sessionToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task StopWorkAsync(string environmentId, string workId, bool force, CancellationToken cancellationToken = default)
        {
            StopWorkCalls.Add((environmentId, workId, force));
            return Task.CompletedTask;
        }

        public Task ArchiveSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            ArchiveCalls.Add(sessionId);
            return Task.CompletedTask;
        }
    }

    private sealed class StubCapacityWake(Action onWake) : ICapacityWake
    {
        public CapacitySignal CreateSignal() => new(CancellationToken.None, () => { });
        public void Wake() => onWake();
    }

    private sealed class StubSessionHandle(string sessionId, IReadOnlyList<string>? lastStderr = null) : ISessionHandle
    {
        public string SessionId { get; } = sessionId;
        public Task<SessionDoneStatus> Done { get; } = Task.FromResult(SessionDoneStatus.Completed);
        public IReadOnlyList<SessionActivity> Activities { get; } = [];
        public SessionActivity? CurrentActivity => null;
        public string AccessToken { get; private set; } = string.Empty;
        public IReadOnlyList<string> LastStderr { get; } = lastStderr ?? [];
        public void Kill()
        {
        }

        public void ForceKill()
        {
        }

        public void WriteStdin(string data)
        {
        }

        public void UpdateAccessToken(string token)
        {
            AccessToken = token;
        }
    }

    private sealed class StubBridgeLogger : IBridgeLogger
    {
        public List<string> VerboseMessages { get; } = [];
        public List<(string SessionId, long DurationMs)> SessionCompleteCalls { get; } = [];
        public List<(string SessionId, string Error)> SessionFailedCalls { get; } = [];
        public List<string> RemovedSessions { get; } = [];

        public void PrintBanner(BridgeConfig config, string environmentId) => throw new NotSupportedException();
        public void LogSessionStart(string sessionId, string prompt) => throw new NotSupportedException();
        public void LogSessionComplete(string sessionId, long durationMs) => SessionCompleteCalls.Add((sessionId, durationMs));
        public void LogSessionFailed(string sessionId, string error) => SessionFailedCalls.Add((sessionId, error));
        public void LogStatus(string message) => throw new NotSupportedException();
        public void LogVerbose(string message) => VerboseMessages.Add(message);
        public void LogError(string message) => throw new NotSupportedException();
        public void LogReconnected(long disconnectedMs) => throw new NotSupportedException();
        public void UpdateIdleStatus() => throw new NotSupportedException();
        public void UpdateReconnectingStatus(string delay, string elapsed) => throw new NotSupportedException();
        public void UpdateSessionStatus(string sessionId, string elapsed, SessionActivity activity, IReadOnlyList<string> trail) => throw new NotSupportedException();
        public void ClearStatus()
        {
        }

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
        public void RemoveSession(string sessionId) => RemovedSessions.Add(sessionId);
        public void RefreshDisplay() => throw new NotSupportedException();
    }
}
