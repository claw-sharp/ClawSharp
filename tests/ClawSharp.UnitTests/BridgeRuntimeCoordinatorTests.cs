// TS origin: ./bridge/bridgeMain.ts
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgeRuntimeCoordinatorTests
{
    [Fact]
    public async Task RunAsync_Sequences_Startup_Loop_And_Shutdown()
    {
        var startupCoordinator = new BridgeStartupCoordinator(
            new BridgeStartupDependencies(
                Api: new StubBridgeApiClient(),
                GetBridgeSessionAsync: (_, _) => Task.FromResult<BridgeSessionSummary?>(null),
                CreateSessionAsync: (_, title, _) => Task.FromResult<string?>($"session-for-{title}"),
                WriteBridgePointerAsync: (_, _, _, _) => Task.CompletedTask));
        var shutdownLogger = new StubBridgeLogger();
        var shutdownCoordinator = new BridgeShutdownCoordinator(
            new BridgeShutdownDependencies(
                new StubBridgeApiClient(),
                shutdownLogger,
                (_, _) => Task.CompletedTask,
                SleepAsync: (_, _) => Task.CompletedTask));

        BridgeConfig? loopConfig = null;
        string? loopEnvironmentId = null;
        string? loopEnvironmentSecret = null;
        string? loopInitialSessionId = null;
        var runtime = new BridgeRuntimeCoordinator(
            new BridgeRuntimeDependencies(
                startupCoordinator,
                shutdownCoordinator,
                shutdownLogger,
                (config, environmentId, environmentSecret, initialSessionId, _) =>
                {
                    loopConfig = config;
                    loopEnvironmentId = environmentId;
                    loopEnvironmentSecret = environmentSecret;
                    loopInitialSessionId = initialSessionId;
                    return Task.FromResult(new BridgeRuntimeLoopResult(
                        FatalExit: false,
                        ActiveSessions: new Dictionary<string, ISessionHandle>(),
                        SessionWorkIds: new Dictionary<string, string>(),
                        SessionCompatIds: new Dictionary<string, string>()));
                }));

        var result = await runtime.RunAsync(new BridgeRuntimeRequest(
            new BridgeStartupRequest(
                CreateConfig(),
                PreCreateSession: true,
                Title: "named"),
            AllowResumeOnShutdown: true));

        Assert.NotNull(loopConfig);
        Assert.Equal("env_123", loopEnvironmentId);
        Assert.Equal("secret_123", loopEnvironmentSecret);
        Assert.Equal("session-for-named", loopInitialSessionId);
        Assert.Equal("env_123", result.Startup.EnvironmentId);
        Assert.False(result.Loop.FatalExit);
        Assert.True(result.Shutdown.ResumableShutdown);
    }

    [Fact]
    public async Task RunAsync_Passes_Fatal_Loop_Result_Into_Shutdown()
    {
        var startupCoordinator = new BridgeStartupCoordinator(
            new BridgeStartupDependencies(
                Api: new StubBridgeApiClient(),
                GetBridgeSessionAsync: (_, _) => Task.FromResult<BridgeSessionSummary?>(null),
                CreateSessionAsync: (_, title, _) => Task.FromResult<string?>($"session-for-{title}"),
                WriteBridgePointerAsync: (_, _, _, _) => Task.CompletedTask));
        var shutdownLogger = new StubBridgeLogger();
        var shutdownCoordinator = new BridgeShutdownCoordinator(
            new BridgeShutdownDependencies(
                new StubBridgeApiClient(),
                shutdownLogger,
                (_, _) => Task.CompletedTask,
                SleepAsync: (_, _) => Task.CompletedTask));
        var runtime = new BridgeRuntimeCoordinator(
            new BridgeRuntimeDependencies(
                startupCoordinator,
                shutdownCoordinator,
                shutdownLogger,
                (_, _, _, _, _) => Task.FromResult(new BridgeRuntimeLoopResult(
                    FatalExit: true,
                    ActiveSessions: new Dictionary<string, ISessionHandle>(),
                    SessionWorkIds: new Dictionary<string, string>(),
                    SessionCompatIds: new Dictionary<string, string>()))));

        var result = await runtime.RunAsync(new BridgeRuntimeRequest(
            new BridgeStartupRequest(
                CreateConfig(),
                PreCreateSession: true,
                Title: "named"),
            AllowResumeOnShutdown: true));

        Assert.False(result.Shutdown.ResumableShutdown);
        Assert.True(result.Shutdown.DeregisterAttempted);
    }

    [Fact]
    public async Task RunAsync_Bubbles_Startup_Failure_And_Skips_Loop()
    {
        var startupCoordinator = new BridgeStartupCoordinator(
            new BridgeStartupDependencies(
                Api: new StubBridgeApiClient(),
                GetBridgeSessionAsync: (_, _) => Task.FromResult<BridgeSessionSummary?>(null),
                CreateSessionAsync: (_, _, _) => throw new InvalidOperationException("startup failed"),
                WriteBridgePointerAsync: (_, _, _, _) => Task.CompletedTask));
        var shutdownLogger = new StubBridgeLogger();
        var shutdownCoordinator = new BridgeShutdownCoordinator(
            new BridgeShutdownDependencies(
                new StubBridgeApiClient(),
                shutdownLogger,
                (_, _) => Task.CompletedTask,
                SleepAsync: (_, _) => Task.CompletedTask));
        var loopCalls = 0;
        var runtime = new BridgeRuntimeCoordinator(
            new BridgeRuntimeDependencies(
                startupCoordinator,
                shutdownCoordinator,
                shutdownLogger,
                (_, _, _, _, _) =>
                {
                    loopCalls++;
                    return Task.FromResult(new BridgeRuntimeLoopResult(
                        false,
                        new Dictionary<string, ISessionHandle>(),
                        new Dictionary<string, string>(),
                        new Dictionary<string, string>()));
                }));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.RunAsync(new BridgeRuntimeRequest(
                new BridgeStartupRequest(CreateConfig(), PreCreateSession: true))));

        Assert.Equal("startup failed", error.Message);
        Assert.Equal(0, loopCalls);
    }

    private static BridgeConfig CreateConfig()
    {
        return new BridgeConfig(
            Dir: "D:\\repo",
            MachineName: "machine",
            Branch: "main",
            GitRepoUrl: "https://example.com/repo.git",
            MaxSessions: 1,
            SpawnMode: SpawnMode.SingleSession,
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
        public Task<BridgeRegisterEnvironmentResponse> RegisterBridgeEnvironmentAsync(BridgeConfig config, CancellationToken cancellationToken = default)
            => Task.FromResult(new BridgeRegisterEnvironmentResponse("env_123", "secret_123"));

        public Task<BridgeWorkResponse?> PollForWorkAsync(string environmentId, string environmentSecret, CancellationToken cancellationToken = default, int? reclaimOlderThanMs = null)
            => throw new NotSupportedException();

        public Task AcknowledgeWorkAsync(string environmentId, string workId, string sessionToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task StopWorkAsync(string environmentId, string workId, bool force, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task DeregisterEnvironmentAsync(string environmentId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SendPermissionResponseEventAsync(string sessionId, PermissionResponseEvent @event, string sessionToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task ArchiveSessionAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ReconnectSessionAsync(string environmentId, string sessionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BridgeHeartbeatResponse> HeartbeatWorkAsync(string environmentId, string workId, string sessionToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubBridgeLogger : IBridgeLogger
    {
        public void PrintBanner(BridgeConfig config, string environmentId) => throw new NotSupportedException();
        public void LogSessionStart(string sessionId, string prompt) => throw new NotSupportedException();
        public void LogSessionComplete(string sessionId, long durationMs) => throw new NotSupportedException();
        public void LogSessionFailed(string sessionId, string error) => throw new NotSupportedException();
        public void LogStatus(string message) { }
        public void LogVerbose(string message) { }
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
