using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgeTransportReconnectCoordinatorTests
{
    [Fact]
    public async Task TryReconnectInPlaceAsync_Tries_Compat_Then_Infra_Session_Id()
    {
        var api = new StubBridgeApiClient
        {
            ReconnectErrors = new Queue<Exception?>([new InvalidOperationException("missing"), null])
        };
        var state = CreateState();
        var coordinator = new BridgeTransportReconnectCoordinator(
            CreateDependencies(api, state));

        var result = await coordinator.TryReconnectInPlaceAsync("env_123", "session_123");

        Assert.True(result);
        Assert.Equal(["session_123", "cse_123"], api.ReconnectAttempts);
    }

    [Fact]
    public async Task ReconnectEnvironmentWithSessionAsync_Reuses_InFlight_Task()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = new StubBridgeApiClient
        {
            RegisterFunc = async (_, _) =>
            {
                await gate.Task;
                return new BridgeRegisterEnvironmentResponse("env_123", "secret_2");
            }
        };
        var state = CreateState();
        var createSessionCalls = 0;
        var coordinator = new BridgeTransportReconnectCoordinator(
            CreateDependencies(
                api,
                state,
                createSessionAsync: (_, _, _) =>
                {
                    createSessionCalls++;
                    return Task.FromResult<string?>("session_456");
                }));

        var first = coordinator.ReconnectEnvironmentWithSessionAsync();
        var second = coordinator.ReconnectEnvironmentWithSessionAsync();
        gate.SetResult();

        Assert.True(await first);
        Assert.True(await second);
        Assert.Equal(1, api.RegisterCalls);
        Assert.Equal(1, createSessionCalls);
    }

    [Fact]
    public async Task ReconnectEnvironmentWithSessionAsync_Archives_And_Creates_Fresh_Session_When_InPlace_Reconnect_Fails()
    {
        var api = new StubBridgeApiClient
        {
            RegisterResponse = new BridgeRegisterEnvironmentResponse("env_456", "secret_2"),
            ReconnectError = new InvalidOperationException("not found")
        };
        var state = CreateState();
        var archivedSessions = new List<string>();
        var pointerWrites = new List<(string SessionId, string EnvironmentId)>();
        var publishedIds = new List<string?>();
        var recentInboundCleared = 0;
        var flushedCleared = 0;
        var userCallbackResets = 0;
        var debugMessages = new List<string>();
        var coordinator = new BridgeTransportReconnectCoordinator(
            CreateDependencies(
                api,
                state,
                archiveSessionAsync: (sessionId, _) =>
                {
                    archivedSessions.Add(sessionId);
                    return Task.CompletedTask;
                },
                createSessionAsync: (_, _, _) => Task.FromResult<string?>("session_456"),
                writeBridgePointerAsync: (sessionId, environmentId, _) =>
                {
                    pointerWrites.Add((sessionId, environmentId));
                    return Task.CompletedTask;
                },
                publishSessionBridgeIdAsync: sessionId =>
                {
                    publishedIds.Add(sessionId);
                    return Task.CompletedTask;
                },
                clearRecentInboundUuids: () => recentInboundCleared++,
                clearPreviouslyFlushedUuids: () => flushedCleared++,
                resetUserMessageCallback: () => userCallbackResets++,
                onDebug: debugMessages.Add));

        var result = await coordinator.ReconnectEnvironmentWithSessionAsync();

        Assert.True(result);
        Assert.Equal("env_456", state.EnvironmentId);
        Assert.Equal("secret_2", state.EnvironmentSecret);
        Assert.Equal("session_456", state.CurrentSessionId);
        Assert.Null(state.CurrentWorkId);
        Assert.Null(state.CurrentIngressToken);
        Assert.Equal(0, state.LastTransportSequenceNum);
        Assert.Equal(0, state.EnvironmentRecreations);
        Assert.Equal(["session_123"], archivedSessions);
        Assert.Equal([("session_456", "env_456")], pointerWrites);
        Assert.Equal(["session_456"], publishedIds);
        Assert.Equal(1, recentInboundCleared);
        Assert.Equal(1, flushedCleared);
        Assert.Equal(1, userCallbackResets);
        Assert.Contains(debugMessages, message => message.Contains("Re-created session: session_456", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReconnectEnvironmentWithSessionAsync_Returns_False_When_Limit_Exceeded()
    {
        var api = new StubBridgeApiClient();
        var state = CreateState();
        state.EnvironmentRecreations = 3;
        var coordinator = new BridgeTransportReconnectCoordinator(
            CreateDependencies(api, state));

        var result = await coordinator.ReconnectEnvironmentWithSessionAsync();

        Assert.False(result);
        Assert.Equal(4, state.EnvironmentRecreations);
        Assert.Equal(0, api.RegisterCalls);
    }

    [Fact]
    public async Task HandleTransportPermanentCloseAsync_Clean_Close_Aborts_And_Teardowns()
    {
        var api = new StubBridgeApiClient();
        var state = CreateState();
        state.Transport = new StubTransport { SequenceNum = 42 };
        var states = new List<(string State, string? Message)>();
        var abortCalls = 0;
        var teardownCalls = 0;
        var coordinator = new BridgeTransportReconnectCoordinator(
            CreateDependencies(
                api,
                state,
                onStateChange: (bridgeState, message) => states.Add((bridgeState, message)),
                abortPollLoop: () => abortCalls++,
                triggerTeardown: () => teardownCalls++));

        await coordinator.HandleTransportPermanentCloseAsync(1000);

        Assert.Null(state.Transport);
        Assert.Equal(42, state.LastTransportSequenceNum);
        Assert.Equal([("failed", "session ended")], states);
        Assert.Equal(1, abortCalls);
        Assert.Equal(1, teardownCalls);
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
            CurrentIngressToken = "ingress_123",
            LastTransportSequenceNum = 99
        };
    }

    private static BridgeTransportReconnectDependencies CreateDependencies(
        StubBridgeApiClient api,
        BridgeTransportReconnectState state,
        Func<string, string?, CancellationToken, Task<string?>>? createSessionAsync = null,
        Func<string, CancellationToken, Task>? archiveSessionAsync = null,
        Func<string, string, CancellationToken, Task>? writeBridgePointerAsync = null,
        Func<string?, Task>? publishSessionBridgeIdAsync = null,
        Action? clearRecentInboundUuids = null,
        Action? clearPreviouslyFlushedUuids = null,
        Action? resetUserMessageCallback = null,
        Action<string>? onDebug = null,
        Action<string, string?>? onStateChange = null,
        Action? triggerTeardown = null,
        Action? abortPollLoop = null,
        Func<int>? dropFlushGate = null,
        Func<bool>? isPollAborted = null,
        Action? wakePollLoop = null)
    {
        return new BridgeTransportReconnectDependencies(
            Api: api,
            State: state,
            WakePollLoop: wakePollLoop ?? (() => { }),
            DropFlushGate: dropFlushGate ?? (() => 0),
            IsPollAborted: isPollAborted ?? (() => false),
            GetCurrentTitle: () => "Title",
            CreateSessionAsync: createSessionAsync ?? ((_, _, _) => Task.FromResult<string?>("session_456")),
            ArchiveSessionAsync: archiveSessionAsync ?? ((_, _) => Task.CompletedTask),
            WriteBridgePointerAsync: writeBridgePointerAsync ?? ((_, _, _) => Task.CompletedTask),
            ClearRecentInboundUuids: clearRecentInboundUuids ?? (() => { }),
            ResetUserMessageCallback: resetUserMessageCallback,
            ClearPreviouslyFlushedUuids: clearPreviouslyFlushedUuids,
            OnDebug: onDebug,
            OnStateChange: onStateChange,
            TriggerTeardown: triggerTeardown,
            AbortPollLoop: abortPollLoop,
            PublishSessionBridgeIdAsync: publishSessionBridgeIdAsync);
    }

    private sealed class StubBridgeApiClient : IBridgeApiClient
    {
        public BridgeRegisterEnvironmentResponse RegisterResponse { get; init; } = new("env_123", "secret_1");
        public Func<BridgeConfig, CancellationToken, Task<BridgeRegisterEnvironmentResponse>>? RegisterFunc { get; init; }
        public int RegisterCalls { get; private set; }
        public Exception? ReconnectError { get; init; }
        public Queue<Exception?> ReconnectErrors { get; init; } = [];
        public List<string> ReconnectAttempts { get; } = [];

        public Task<BridgeRegisterEnvironmentResponse> RegisterBridgeEnvironmentAsync(BridgeConfig config, CancellationToken cancellationToken = default)
        {
            RegisterCalls++;
            return RegisterFunc?.Invoke(config, cancellationToken) ?? Task.FromResult(RegisterResponse);
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
            return Task.CompletedTask;
        }

        public Task DeregisterEnvironmentAsync(string environmentId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task SendPermissionResponseEventAsync(string sessionId, PermissionResponseEvent @event, string sessionToken, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task ArchiveSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ReconnectSessionAsync(string environmentId, string sessionId, CancellationToken cancellationToken = default)
        {
            ReconnectAttempts.Add(sessionId);
            if (ReconnectErrors.Count > 0)
            {
                var error = ReconnectErrors.Dequeue();
                if (error is not null)
                {
                    throw error;
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
            throw new NotImplementedException();
        }
    }

    private sealed class StubTransport : IBridgeReplTransport
    {
        public long SequenceNum { get; init; }

        public long GetLastSequenceNum() => SequenceNum;

        public void Close()
        {
        }
    }
}
