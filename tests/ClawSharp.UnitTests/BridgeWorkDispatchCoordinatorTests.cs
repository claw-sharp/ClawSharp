// TS origin: ./bridge/bridgeMain.ts
using System.Text;
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgeWorkDispatchCoordinatorTests
{
    [Fact]
    public async Task DispatchAsync_Healthcheck_Acknowledges_And_Logs()
    {
        var api = new StubBridgeApiClient();
        var logger = new StubBridgeLogger();
        var coordinator = CreateCoordinator(api, logger);

        var result = await coordinator.DispatchAsync(new BridgeWorkDispatchRequest(
            CreateConfig(),
            EnvironmentId: "env_123",
            Work: CreateWork("work_123", BridgeWorkDataType.Healthcheck, "session_123"),
            AtCapacityBeforeSwitch: false,
            State: CreateState()));

        Assert.True(result.Handled);
        Assert.Equal([("env_123", "work_123")], api.AcknowledgeCalls.Select(call => (call.EnvironmentId, call.WorkId)).ToArray());
        Assert.Contains("Healthcheck received", logger.VerboseMessages);
    }

    [Fact]
    public async Task DispatchAsync_WhenExistingSession_Refreshes_Token_And_Acknowledges()
    {
        var api = new StubBridgeApiClient();
        var logger = new StubBridgeLogger();
        var handle = new StubSessionHandle("session_123");
        var state = CreateState();
        state.ActiveSessions["session_123"] = handle;
        state.SessionCompatIds["session_123"] = "session_123";
        var refreshed = new List<string>();
        var coordinator = CreateCoordinator(
            api,
            logger,
            onExistingSessionRefreshedAsync: (sessionId, _, workId, _, _) =>
            {
                refreshed.Add($"{sessionId}:{workId}");
                return Task.CompletedTask;
            });

        var result = await coordinator.DispatchAsync(new BridgeWorkDispatchRequest(
            CreateConfig(),
            EnvironmentId: "env_123",
            Work: CreateWork("work_123", BridgeWorkDataType.Session, "session_123"),
            AtCapacityBeforeSwitch: true,
            State: state));

        Assert.True(result.RefreshedExistingSession);
        Assert.Equal("session-token", handle.AccessToken);
        Assert.Equal("work_123", state.SessionWorkIds["session_123"]);
        Assert.Equal(["session_123:work_123"], refreshed);
        Assert.Single(api.AcknowledgeCalls);
    }

    [Fact]
    public async Task DispatchAsync_WhenAtCapacity_DoesNotAcknowledge_NewSession()
    {
        var api = new StubBridgeApiClient();
        var logger = new StubBridgeLogger();
        var state = CreateState();
        state.ActiveSessions["session_1"] = new StubSessionHandle("session_1");
        var coordinator = CreateCoordinator(api, logger);

        var result = await coordinator.DispatchAsync(new BridgeWorkDispatchRequest(
            CreateConfig(maxSessions: 1),
            EnvironmentId: "env_123",
            Work: CreateWork("work_123", BridgeWorkDataType.Session, "session_123"),
            AtCapacityBeforeSwitch: true,
            State: state));

        Assert.True(result.AtCapacityRefused);
        Assert.Empty(api.AcknowledgeCalls);
        Assert.Empty(state.SessionWorkIds);
    }

    [Fact]
    public async Task DispatchAsync_WhenSecretDecodeFails_StopsWork_And_Marks_Completed()
    {
        var api = new StubBridgeApiClient();
        var logger = new StubBridgeLogger();
        var state = CreateState();
        var coordinator = CreateCoordinator(api, logger);
        var work = new BridgeWorkResponse(
            Id: "work_123",
            Type: "work",
            EnvironmentId: "env_123",
            State: "running",
            Data: new BridgeWorkData(BridgeWorkDataType.Session, "session_123"),
            Secret: "bad-secret",
            CreatedAt: "2026-04-02T12:00:00Z");

        var result = await coordinator.DispatchAsync(new BridgeWorkDispatchRequest(
            CreateConfig(),
            EnvironmentId: "env_123",
            Work: work,
            AtCapacityBeforeSwitch: false,
            State: state));

        Assert.True(result.StopWorkScheduled);
        Assert.Contains("work_123", state.CompletedWorkIds);
        Assert.Equal([("env_123", "work_123", false)], api.StopWorkCalls);
    }

    [Fact]
    public async Task DispatchAsync_Spawns_CcrV2_Session_After_RegisterWorker_Retry()
    {
        var api = new StubBridgeApiClient();
        var logger = new StubBridgeLogger();
        var spawner = new StubSessionSpawner();
        var sleepCalls = new List<double>();
        var registerAttempts = 0;
        var spawned = new List<(string SessionId, bool? UseCcrV2, long? WorkerEpoch, string Dir)>();
        var coordinator = CreateCoordinator(
            api,
            logger,
            spawner,
            registerWorkerAsync: (_, _) =>
            {
                registerAttempts++;
                if (registerAttempts == 1)
                {
                    throw new InvalidOperationException("temporary");
                }

                return Task.FromResult(17L);
            },
            sleepAsync: (delay, _) =>
            {
                sleepCalls.Add(delay);
                return Task.CompletedTask;
            },
            onSessionSpawnedAsync: (sessionId, _, _, _, useCcrV2, workerEpoch, _, _) =>
            {
                spawned.Add((sessionId, useCcrV2, workerEpoch, spawner.LastDir!));
                return Task.CompletedTask;
            });

        var result = await coordinator.DispatchAsync(new BridgeWorkDispatchRequest(
            CreateConfig(),
            EnvironmentId: "env_123",
            Work: CreateWork("work_123", BridgeWorkDataType.Session, "cse_123", useCodeSessions: true),
            AtCapacityBeforeSwitch: false,
            State: CreateState()));

        Assert.True(result.SpawnedSession);
        Assert.Equal(2, registerAttempts);
        Assert.Equal([2000d], sleepCalls);
        Assert.Equal(("cse_123", true, 17L, "D:\\repo"), spawned.Single());
        Assert.Equal("session_123", result.CompatSessionId);
        Assert.Single(api.AcknowledgeCalls);
    }

    [Fact]
    public async Task DispatchAsync_WhenSpawnFails_Removes_Worktree_And_StopsWork()
    {
        var api = new StubBridgeApiClient();
        var logger = new StubBridgeLogger();
        var removedWorktrees = new List<string>();
        var coordinator = CreateCoordinator(
            api,
            logger,
            new StubSessionSpawner { SpawnException = new InvalidOperationException("spawn failed") },
            createAgentWorktreeAsync: (_, _) => Task.FromResult(new BridgeSessionWorktree("D:\\repo\\.wt\\bridge-session_123")),
            removeAgentWorktreeAsync: (worktree, _) =>
            {
                removedWorktrees.Add(worktree.WorktreePath);
                return Task.CompletedTask;
            });

        var state = CreateState();
        var result = await coordinator.DispatchAsync(new BridgeWorkDispatchRequest(
            CreateConfig(spawnMode: SpawnMode.Worktree),
            EnvironmentId: "env_123",
            Work: CreateWork("work_123", BridgeWorkDataType.Session, "session_123"),
            AtCapacityBeforeSwitch: false,
            State: state));

        Assert.True(result.StopWorkScheduled);
        Assert.Equal(["D:\\repo\\.wt\\bridge-session_123"], removedWorktrees);
        Assert.Equal([("env_123", "work_123", false)], api.StopWorkCalls);
        Assert.Empty(state.SessionWorktrees);
    }

    private static BridgeWorkDispatchCoordinator CreateCoordinator(
        StubBridgeApiClient api,
        StubBridgeLogger logger,
        StubSessionSpawner? spawner = null,
        Func<string, CancellationToken, Task<long>>? registerWorkerAsync = null,
        Func<string, CancellationToken, Task<BridgeSessionWorktree>>? createAgentWorktreeAsync = null,
        Func<BridgeSessionWorktree, CancellationToken, Task>? removeAgentWorktreeAsync = null,
        Func<double, CancellationToken, Task>? sleepAsync = null,
        Func<string, ISessionHandle, string, BridgeWorkSecret, bool, long?, BridgeSessionWorktree?, CancellationToken, Task>? onSessionSpawnedAsync = null,
        Func<string, ISessionHandle, string, BridgeWorkSecret, CancellationToken, Task>? onExistingSessionRefreshedAsync = null)
    {
        return new BridgeWorkDispatchCoordinator(new BridgeWorkDispatchDependencies(
            Api: api,
            Logger: logger,
            Spawner: spawner ?? new StubSessionSpawner(),
            StopWorkRetryDependencies: new BridgeStopWorkRetryDependencies(api, logger),
            RegisterWorkerAsync: registerWorkerAsync,
            CreateAgentWorktreeAsync: createAgentWorktreeAsync,
            RemoveAgentWorktreeAsync: removeAgentWorktreeAsync,
            SleepAsync: sleepAsync,
            DeriveSessionTitle: text => text.Trim(),
            OnSessionSpawnedAsync: onSessionSpawnedAsync,
            OnExistingSessionRefreshedAsync: onExistingSessionRefreshedAsync));
    }

    private static BridgeWorkDispatchState CreateState()
    {
        return new BridgeWorkDispatchState(
            ActiveSessions: new Dictionary<string, ISessionHandle>(StringComparer.Ordinal),
            SessionWorkIds: new Dictionary<string, string>(StringComparer.Ordinal),
            SessionIngressTokens: new Dictionary<string, string>(StringComparer.Ordinal),
            SessionCompatIds: new Dictionary<string, string>(StringComparer.Ordinal),
            SessionStartTimes: new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal),
            SessionWorktrees: new Dictionary<string, BridgeSessionWorktree>(StringComparer.Ordinal),
            CompletedWorkIds: new HashSet<string>(StringComparer.Ordinal),
            V2Sessions: new HashSet<string>(StringComparer.Ordinal),
            TitledSessions: new HashSet<string>(StringComparer.Ordinal));
    }

    private static BridgeConfig CreateConfig(int maxSessions = 32, SpawnMode spawnMode = SpawnMode.SameDir)
    {
        return new BridgeConfig(
            Dir: "D:\\repo",
            MachineName: "machine",
            Branch: "main",
            GitRepoUrl: "https://example.com/repo.git",
            MaxSessions: maxSessions,
            SpawnMode: spawnMode,
            Verbose: false,
            Sandbox: false,
            BridgeId: "bridge_123",
            WorkerType: "claude_code",
            EnvironmentId: "env_client",
            ApiBaseUrl: "https://api.example.com",
            SessionIngressUrl: "https://ingress.example.com");
    }

    private static BridgeWorkResponse CreateWork(string workId, BridgeWorkDataType dataType, string sessionId, bool useCodeSessions = false)
    {
        return new BridgeWorkResponse(
            Id: workId,
            Type: "work",
            EnvironmentId: "env_123",
            State: "running",
            Data: new BridgeWorkData(dataType, sessionId),
            Secret: CreateSecret(useCodeSessions),
            CreatedAt: "2026-04-02T12:00:00Z");
    }

    private static string CreateSecret(bool useCodeSessions)
    {
        var json = $$"""
        {"version":1,"session_ingress_token":"session-token","api_base_url":"https://api.example.com","sources":[],"auth":[],"use_code_sessions":{{useCodeSessions.ToString().ToLowerInvariant()}}}
        """;
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private sealed class StubBridgeApiClient : IBridgeApiClient
    {
        public List<(string EnvironmentId, string WorkId, string SessionToken)> AcknowledgeCalls { get; } = [];
        public List<(string EnvironmentId, string WorkId, bool Force)> StopWorkCalls { get; } = [];

        public Task<BridgeRegisterEnvironmentResponse> RegisterBridgeEnvironmentAsync(BridgeConfig config, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<BridgeWorkResponse?> PollForWorkAsync(string environmentId, string environmentSecret, CancellationToken cancellationToken = default, int? reclaimOlderThanMs = null)
            => throw new NotSupportedException();

        public Task AcknowledgeWorkAsync(string environmentId, string workId, string sessionToken, CancellationToken cancellationToken = default)
        {
            AcknowledgeCalls.Add((environmentId, workId, sessionToken));
            return Task.CompletedTask;
        }

        public Task StopWorkAsync(string environmentId, string workId, bool force, CancellationToken cancellationToken = default)
        {
            StopWorkCalls.Add((environmentId, workId, force));
            return Task.CompletedTask;
        }

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

    private sealed class StubSessionHandle(string sessionId) : ISessionHandle
    {
        public string SessionId { get; } = sessionId;
        public Task<SessionDoneStatus> Done { get; } = Task.FromResult(SessionDoneStatus.Completed);
        public IReadOnlyList<SessionActivity> Activities { get; } = [];
        public SessionActivity? CurrentActivity => null;
        public string AccessToken { get; private set; } = "old-token";
        public IReadOnlyList<string> LastStderr { get; } = [];
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

    private sealed class StubSessionSpawner : ISessionSpawner
    {
        public Exception? SpawnException { get; init; }
        public string? LastDir { get; private set; }
        public SessionSpawnOptions? LastOptions { get; private set; }

        public ISessionHandle Spawn(SessionSpawnOptions options, string dir)
        {
            LastDir = dir;
            LastOptions = options;
            if (SpawnException is not null)
            {
                throw SpawnException;
            }

            var handle = new StubSessionHandle(options.SessionId);
            handle.UpdateAccessToken(options.AccessToken);
            return handle;
        }
    }

    private sealed class StubBridgeLogger : IBridgeLogger
    {
        public List<string> VerboseMessages { get; } = [];
        public void PrintBanner(BridgeConfig config, string environmentId) => throw new NotSupportedException();
        public void LogSessionStart(string sessionId, string prompt)
        {
        }

        public void LogSessionComplete(string sessionId, long durationMs) => throw new NotSupportedException();
        public void LogSessionFailed(string sessionId, string error) => throw new NotSupportedException();
        public void LogStatus(string message) => throw new NotSupportedException();
        public void LogVerbose(string message) => VerboseMessages.Add(message);
        public void LogError(string message)
        {
        }

        public void LogReconnected(long disconnectedMs) => throw new NotSupportedException();
        public void UpdateIdleStatus() => throw new NotSupportedException();
        public void UpdateReconnectingStatus(string delay, string elapsed) => throw new NotSupportedException();
        public void UpdateSessionStatus(string sessionId, string elapsed, SessionActivity activity, IReadOnlyList<string> trail) => throw new NotSupportedException();
        public void ClearStatus() => throw new NotSupportedException();
        public void SetRepoInfo(string repoName, string branch) => throw new NotSupportedException();
        public void SetDebugLogPath(string path) => throw new NotSupportedException();
        public void SetAttached(string sessionId)
        {
        }

        public void UpdateFailedStatus(string error) => throw new NotSupportedException();
        public void ToggleQr() => throw new NotSupportedException();
        public void UpdateSessionCount(int active, int max, SpawnMode mode) => throw new NotSupportedException();
        public void SetSpawnModeDisplay(SpawnMode? mode) => throw new NotSupportedException();
        public void AddSession(string sessionId, string url)
        {
        }

        public void UpdateSessionActivity(string sessionId, SessionActivity activity) => throw new NotSupportedException();
        public void SetSessionTitle(string sessionId, string title)
        {
        }

        public void RemoveSession(string sessionId) => throw new NotSupportedException();
        public void RefreshDisplay() => throw new NotSupportedException();
    }
}
