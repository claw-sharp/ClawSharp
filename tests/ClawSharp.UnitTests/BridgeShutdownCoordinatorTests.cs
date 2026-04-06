using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgeShutdownCoordinatorTests
{
    [Fact]
    public async Task ShutdownAsync_Resumable_Single_Session_Skips_Archive_Deregister_And_Pointer_Clear()
    {
        var api = new StubBridgeApiClient();
        var logger = new StubBridgeLogger();
        var clearedPointers = 0;
        var coordinator = new BridgeShutdownCoordinator(
            new BridgeShutdownDependencies(
                api,
                logger,
                (_, _) =>
                {
                    clearedPointers++;
                    return Task.CompletedTask;
                }));

        var result = await coordinator.ShutdownAsync(new BridgeShutdownRequest(
            Config: CreateConfig(SpawnMode.SingleSession),
            EnvironmentId: "env_123",
            FatalExit: false,
            InitialSessionId: "session_123",
            ActiveSessions: new Dictionary<string, ISessionHandle>(),
            SessionWorkIds: new Dictionary<string, string>(),
            SessionCompatIds: new Dictionary<string, string>(),
            AllowResumeOnShutdown: true));

        Assert.True(result.ResumableShutdown);
        Assert.Equal(0, result.ArchivedSessionCount);
        Assert.False(result.DeregisterAttempted);
        Assert.False(result.PointerCleared);
        Assert.Equal(0, clearedPointers);
        Assert.Equal(
            "Resume this session by running `clawsharp remote-control --continue`",
            logger.StatusMessages.Single());
        Assert.Empty(api.ArchiveCalls);
        Assert.Empty(api.DeregisterCalls);
    }

    [Fact]
    public async Task ShutdownAsync_Kills_ForceKills_Removes_Worktrees_Stops_Work_Archives_And_Clears_Pointer()
    {
        var api = new StubBridgeApiClient();
        var logger = new StubBridgeLogger();
        var removedWorktrees = new List<string>();
        var clearedPointerDirs = new List<string>();
        var sessionHandle = new StubSessionHandle();
        var pendingCleanup = Task.CompletedTask;
        var coordinator = new BridgeShutdownCoordinator(
            new BridgeShutdownDependencies(
                api,
                logger,
                (dir, _) =>
                {
                    clearedPointerDirs.Add(dir);
                    return Task.CompletedTask;
                },
                (worktree, _) =>
                {
                    removedWorktrees.Add(worktree.WorktreePath);
                    return Task.CompletedTask;
                },
                SleepAsync: (_, _) => Task.CompletedTask));

        var result = await coordinator.ShutdownAsync(new BridgeShutdownRequest(
            Config: CreateConfig(SpawnMode.SameDir),
            EnvironmentId: "env_123",
            FatalExit: false,
            InitialSessionId: "session_999",
            ActiveSessions: new Dictionary<string, ISessionHandle> { ["session_123"] = sessionHandle },
            SessionWorkIds: new Dictionary<string, string> { ["session_123"] = "work_123" },
            SessionCompatIds: new Dictionary<string, string> { ["session_123"] = "session_123_compat" },
            SessionWorktrees: new Dictionary<string, BridgeShutdownWorktree>
            {
                ["session_123"] = new("D:\\repo-worktree")
            },
            PendingCleanups: [pendingCleanup],
            AllowResumeOnShutdown: false));

        Assert.False(result.ResumableShutdown);
        Assert.Equal(2, result.ArchivedSessionCount);
        Assert.True(result.DeregisterAttempted);
        Assert.True(result.PointerCleared);
        Assert.Equal(1, sessionHandle.KillCalls);
        Assert.Equal(1, sessionHandle.ForceKillCalls);
        Assert.Equal(["D:\\repo-worktree"], removedWorktrees);
        Assert.Equal([("env_123", "work_123", true)], api.StopWorkCalls);
        Assert.Equal(["session_123_compat", "session_999"], api.ArchiveCalls.OrderBy(static value => value).ToArray());
        Assert.Equal(["env_123"], api.DeregisterCalls);
        Assert.Equal(["D:\\repo"], clearedPointerDirs);
        Assert.Contains("Environment deregistered.", logger.VerboseMessages);
        Assert.Contains("Environment offline.", logger.VerboseMessages);
    }

    [Fact]
    public async Task ShutdownAsync_Logs_Verbose_When_StopWork_Archive_Or_Deregister_Fail()
    {
        var api = new StubBridgeApiClient
        {
            StopWorkError = new InvalidOperationException("stop failed"),
            ArchiveError = new InvalidOperationException("archive failed"),
            DeregisterError = new InvalidOperationException("deregister failed")
        };
        var logger = new StubBridgeLogger();
        var coordinator = new BridgeShutdownCoordinator(
            new BridgeShutdownDependencies(
                api,
                logger,
                (_, _) => Task.CompletedTask,
                SleepAsync: (_, _) => Task.CompletedTask));

        await coordinator.ShutdownAsync(new BridgeShutdownRequest(
            Config: CreateConfig(SpawnMode.SameDir),
            EnvironmentId: "env_123",
            FatalExit: true,
            InitialSessionId: "session_123",
            ActiveSessions: new Dictionary<string, ISessionHandle>
            {
                ["session_123"] = new StubSessionHandle()
            },
            SessionWorkIds: new Dictionary<string, string> { ["session_123"] = "work_123" },
            SessionCompatIds: new Dictionary<string, string>()));

        Assert.Contains(logger.VerboseMessages, message => message.Contains("Failed to stop work work_123", StringComparison.Ordinal));
        Assert.Contains(logger.VerboseMessages, message => message.Contains("Failed to archive session session_123", StringComparison.Ordinal));
        Assert.Contains(logger.VerboseMessages, message => message.Contains("Failed to deregister environment", StringComparison.Ordinal));
    }

    private static BridgeConfig CreateConfig(SpawnMode spawnMode)
    {
        return new BridgeConfig(
            Dir: "D:\\repo",
            MachineName: "machine",
            Branch: "main",
            GitRepoUrl: "https://example.com/repo.git",
            MaxSessions: spawnMode == SpawnMode.SingleSession ? 1 : 4,
            SpawnMode: spawnMode,
            Verbose: false,
            Sandbox: false,
            BridgeId: "bridge_123",
            WorkerType: "claude_code",
            EnvironmentId: "env_client",
            ApiBaseUrl: "https://api.example.com",
            SessionIngressUrl: "wss://ingress.example.com");
    }

    private sealed class StubBridgeApiClient : IBridgeApiClient
    {
        public List<(string EnvironmentId, string WorkId, bool Force)> StopWorkCalls { get; } = [];
        public List<string> ArchiveCalls { get; } = [];
        public List<string> DeregisterCalls { get; } = [];
        public Exception? StopWorkError { get; init; }
        public Exception? ArchiveError { get; init; }
        public Exception? DeregisterError { get; init; }

        public Task<BridgeRegisterEnvironmentResponse> RegisterBridgeEnvironmentAsync(BridgeConfig config, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BridgeWorkResponse?> PollForWorkAsync(string environmentId, string environmentSecret, CancellationToken cancellationToken = default, int? reclaimOlderThanMs = null)
            => throw new NotSupportedException();

        public Task AcknowledgeWorkAsync(string environmentId, string workId, string sessionToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task StopWorkAsync(string environmentId, string workId, bool force, CancellationToken cancellationToken = default)
        {
            StopWorkCalls.Add((environmentId, workId, force));
            return StopWorkError is null ? Task.CompletedTask : Task.FromException(StopWorkError);
        }

        public Task DeregisterEnvironmentAsync(string environmentId, CancellationToken cancellationToken = default)
        {
            DeregisterCalls.Add(environmentId);
            return DeregisterError is null ? Task.CompletedTask : Task.FromException(DeregisterError);
        }

        public Task SendPermissionResponseEventAsync(string sessionId, PermissionResponseEvent @event, string sessionToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ArchiveSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            ArchiveCalls.Add(sessionId);
            return ArchiveError is null ? Task.CompletedTask : Task.FromException(ArchiveError);
        }

        public Task ReconnectSessionAsync(string environmentId, string sessionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BridgeHeartbeatResponse> HeartbeatWorkAsync(string environmentId, string workId, string sessionToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubSessionHandle : ISessionHandle
    {
        public int KillCalls { get; private set; }

        public int ForceKillCalls { get; private set; }

        public string SessionId => "session_123";

        public Task<SessionDoneStatus> Done => Task.FromResult(SessionDoneStatus.Completed);

        public IReadOnlyList<SessionActivity> Activities => [];

        public SessionActivity? CurrentActivity => null;

        public string AccessToken => "token";

        public IReadOnlyList<string> LastStderr => [];

        public void Kill()
        {
            KillCalls++;
        }

        public void ForceKill()
        {
            ForceKillCalls++;
        }

        public void WriteStdin(string data)
        {
        }

        public void UpdateAccessToken(string token)
        {
        }
    }

    private sealed class StubBridgeLogger : IBridgeLogger
    {
        public List<string> StatusMessages { get; } = [];

        public List<string> VerboseMessages { get; } = [];

        public void PrintBanner(BridgeConfig config, string environmentId) => throw new NotSupportedException();
        public void LogSessionStart(string sessionId, string prompt) => throw new NotSupportedException();
        public void LogSessionComplete(string sessionId, long durationMs) => throw new NotSupportedException();
        public void LogSessionFailed(string sessionId, string error) => throw new NotSupportedException();
        public void LogStatus(string message) => StatusMessages.Add(message);
        public void LogVerbose(string message) => VerboseMessages.Add(message);
        public void LogError(string message) => throw new NotSupportedException();
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
