// TS origin: ./bridge/bridgeMain.ts
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgeStartupCoordinatorTests
{
    [Fact]
    public async Task StartAsync_Registers_Precreates_And_Writes_Pointer_For_Single_Session()
    {
        var api = new StubBridgeApiClient();
        var pointerWrites = new List<(string Dir, string SessionId, string EnvironmentId)>();
        var coordinator = new BridgeStartupCoordinator(
            CreateDependencies(
                api,
                createSessionAsync: (_, title, _) => Task.FromResult<string?>($"session-for-{title}"),
                writeBridgePointerAsync: (dir, sessionId, environmentId, _) =>
                {
                    pointerWrites.Add((dir, sessionId, environmentId));
                    return Task.CompletedTask;
                }));

        var result = await coordinator.StartAsync(new BridgeStartupRequest(
            CreateConfig(SpawnMode.SingleSession),
            PreCreateSession: true,
            Title: "named-session"));

        Assert.Equal("env_123", result.EnvironmentId);
        Assert.Equal("secret_123", result.EnvironmentSecret);
        Assert.Equal("session-for-named-session", result.InitialSessionId);
        Assert.Null(result.EffectiveResumeSessionId);
        Assert.Equal([("D:\\repo", "session-for-named-session", "env_123")], pointerWrites);
        Assert.Equal(1, api.RegisterCalls);
    }

    [Fact]
    public async Task StartAsync_Resume_Env_Mismatch_Falls_Back_To_Fresh_Session()
    {
        var api = new StubBridgeApiClient
        {
            RegisterResponse = new BridgeRegisterEnvironmentResponse("env_new", "secret_123")
        };
        var coordinator = new BridgeStartupCoordinator(
            CreateDependencies(
                api,
                getBridgeSessionAsync: (_, _) => Task.FromResult<BridgeSessionSummary?>(new BridgeSessionSummary("env_old", "Title")),
                createSessionAsync: (_, _, _) => Task.FromResult<string?>("session_new")));

        var result = await coordinator.StartAsync(new BridgeStartupRequest(
            CreateConfig(SpawnMode.SingleSession),
            PreCreateSession: true,
            ResumeSessionId: "session_123",
            ResumePointerDir: "D:\\repo"));

        Assert.Equal("session_new", result.InitialSessionId);
        Assert.Null(result.EffectiveResumeSessionId);
        Assert.Equal(
            "Warning: Could not resume session session_123 — its environment has expired. Creating a fresh session instead.",
            result.WarningMessage);
        Assert.Empty(api.ReconnectAttempts);
    }

    [Fact]
    public async Task StartAsync_Resume_Tries_Compat_Then_Infra_And_Skips_Precreate_On_Success()
    {
        var api = new StubBridgeApiClient
        {
            ReconnectErrors = new Queue<Exception?>([new InvalidOperationException("missing"), null])
        };
        var createCalls = 0;
        var coordinator = new BridgeStartupCoordinator(
            CreateDependencies(
                api,
                getBridgeSessionAsync: (_, _) => Task.FromResult<BridgeSessionSummary?>(new BridgeSessionSummary("env_123", "Title")),
                createSessionAsync: (_, _, _) =>
                {
                    createCalls++;
                    return Task.FromResult<string?>("session_new");
                }));

        var result = await coordinator.StartAsync(new BridgeStartupRequest(
            CreateConfig(SpawnMode.SingleSession),
            PreCreateSession: true,
            ResumeSessionId: "session_123",
            ResumePointerDir: "D:\\repo"));

        Assert.Equal("session_123", result.InitialSessionId);
        Assert.Equal("session_123", result.EffectiveResumeSessionId);
        Assert.Equal(["session_123", "cse_123"], api.ReconnectAttempts);
        Assert.Equal(0, createCalls);
    }

    [Fact]
    public async Task StartAsync_Resume_Fatal_Reconnect_Clears_Pointer_And_Throws()
    {
        var api = new StubBridgeApiClient
        {
            ReconnectError = new BridgeFatalError("expired", 410, "environment_expired")
        };
        var clearedPointers = new List<string>();
        var coordinator = new BridgeStartupCoordinator(
            CreateDependencies(
                api,
                getBridgeSessionAsync: (_, _) => Task.FromResult<BridgeSessionSummary?>(new BridgeSessionSummary("env_123", "Title")),
                clearBridgePointerAsync: (dir, _) =>
                {
                    clearedPointers.Add(dir);
                    return Task.CompletedTask;
                }));

        var error = await Assert.ThrowsAsync<BridgeFatalError>(() =>
            coordinator.StartAsync(new BridgeStartupRequest(
                CreateConfig(SpawnMode.SingleSession),
                PreCreateSession: true,
                ResumeSessionId: "session_123",
                ResumePointerDir: "D:\\repo")));

        Assert.Equal(410, error.Status);
        Assert.Equal(["D:\\repo"], clearedPointers);
    }

    [Fact]
    public async Task StartAsync_Resume_Transient_Reconnect_Throws_Ts_Shaped_Retry_Message_Without_Clearing_Pointer()
    {
        var api = new StubBridgeApiClient
        {
            ReconnectError = new InvalidOperationException("temporary failure")
        };
        var clearedPointers = new List<string>();
        var coordinator = new BridgeStartupCoordinator(
            CreateDependencies(
                api,
                getBridgeSessionAsync: (_, _) => Task.FromResult<BridgeSessionSummary?>(new BridgeSessionSummary("env_123", "Title")),
                clearBridgePointerAsync: (dir, _) =>
                {
                    clearedPointers.Add(dir);
                    return Task.CompletedTask;
                }));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.StartAsync(new BridgeStartupRequest(
                CreateConfig(SpawnMode.SingleSession),
                PreCreateSession: true,
                ResumeSessionId: "session_123",
                ResumePointerDir: "D:\\repo")));

        Assert.Contains("Failed to reconnect session session_123: temporary failure", error.Message, StringComparison.Ordinal);
        Assert.Contains("The session may still be resumable", error.Message, StringComparison.Ordinal);
        Assert.Empty(clearedPointers);
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
            SessionIngressUrl: "wss://ingress.example.com");
    }

    private static BridgeStartupDependencies CreateDependencies(
        StubBridgeApiClient api,
        Func<string, CancellationToken, Task<BridgeSessionSummary?>>? getBridgeSessionAsync = null,
        Func<string, string?, CancellationToken, Task<string?>>? createSessionAsync = null,
        Func<string, string, string, CancellationToken, Task>? writeBridgePointerAsync = null,
        Func<string, CancellationToken, Task>? clearBridgePointerAsync = null,
        Action<string>? onDebug = null)
    {
        return new BridgeStartupDependencies(
            Api: api,
            GetBridgeSessionAsync: getBridgeSessionAsync ?? ((_, _) => Task.FromResult<BridgeSessionSummary?>(null)),
            CreateSessionAsync: createSessionAsync ?? ((_, _, _) => Task.FromResult<string?>("session_new")),
            WriteBridgePointerAsync: writeBridgePointerAsync ?? ((_, _, _, _) => Task.CompletedTask),
            ClearBridgePointerAsync: clearBridgePointerAsync,
            OnDebug: onDebug);
    }

    private sealed class StubBridgeApiClient : IBridgeApiClient
    {
        public BridgeRegisterEnvironmentResponse RegisterResponse { get; init; } = new("env_123", "secret_123");
        public Exception? ReconnectError { get; init; }
        public Queue<Exception?> ReconnectErrors { get; init; } = [];
        public int RegisterCalls { get; private set; }
        public List<string> ReconnectAttempts { get; } = [];

        public Task<BridgeRegisterEnvironmentResponse> RegisterBridgeEnvironmentAsync(BridgeConfig config, CancellationToken cancellationToken = default)
        {
            RegisterCalls++;
            return Task.FromResult(RegisterResponse);
        }

        public Task<BridgeWorkResponse?> PollForWorkAsync(string environmentId, string environmentSecret, CancellationToken cancellationToken = default, int? reclaimOlderThanMs = null)
        {
            throw new NotSupportedException();
        }

        public Task AcknowledgeWorkAsync(string environmentId, string workId, string sessionToken, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task StopWorkAsync(string environmentId, string workId, bool force, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task DeregisterEnvironmentAsync(string environmentId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SendPermissionResponseEventAsync(string sessionId, PermissionResponseEvent @event, string sessionToken, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task ArchiveSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task ReconnectSessionAsync(string environmentId, string sessionId, CancellationToken cancellationToken = default)
        {
            ReconnectAttempts.Add(sessionId);
            if (ReconnectErrors.Count > 0)
            {
                var next = ReconnectErrors.Dequeue();
                if (next is not null)
                {
                    throw next;
                }

                return Task.CompletedTask;
            }

            if (ReconnectError is not null)
            {
                throw ReconnectError;
            }

            return Task.CompletedTask;
        }

        public Task<BridgeHeartbeatResponse> HeartbeatWorkAsync(string environmentId, string workId, string sessionToken, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
