using System.Text.Json.Nodes;
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class RemoteSessionTransportReconnectRuntimeTests
{
    [Fact]
    public async Task StartAsync_Binds_Initial_Transport_And_Publishes_Handle()
    {
        ResetRegistry();
        var state = CreateState();
        var initialMessages = new[]
        {
            new RemoteSessionOutboundMessage(
                "user",
                "uuid_1",
                new JsonObject
                {
                    ["type"] = "user",
                    ["uuid"] = "uuid_1"
                })
        };
        var transport = new FakeReconnectableTransport();
        var clients = new List<FakeSessionClient>();
        await using var runtime = new RemoteSessionTransportReconnectRuntime(
            new RemoteSessionTransportReconnectRuntimeDependencies(
                ReconnectDependencies: CreateReconnectDependencies(new StubBridgeApiClient(), state),
                ConnectTransportAsync: (_, _) => Task.FromResult<IReconnectableRemoteSessionTransport>(transport),
                CreateSessionClient: (connectedTransport, sessionId) =>
                {
                    var client = new FakeSessionClient(sessionId, connectedTransport);
                    clients.Add(client);
                    return client;
                },
                InitialMessages: initialMessages));

        await runtime.StartAsync();

        Assert.Same(transport, state.Transport);
        Assert.Single(clients);
        Assert.Equal("session_123", clients[0].BridgeSessionId);
        Assert.Single(clients[0].ConnectedPayloads);
        Assert.Same(initialMessages, clients[0].ConnectedPayloads[0]);
        Assert.Equal("session_123", ReplBridgeHandleRegistry.GetSelfBridgeCompatId());
    }

    [Fact]
    public async Task StartAsync_Starts_And_Dispose_Stops_Heartbeat_Runtime_When_Configured()
    {
        ResetRegistry();
        var state = CreateState();
        var transport = new FakeReconnectableTransport();
        var heartbeatRuntime = new FakeHeartbeatRuntime();

        await using (var runtime = new RemoteSessionTransportReconnectRuntime(
                         new RemoteSessionTransportReconnectRuntimeDependencies(
                             ReconnectDependencies: CreateReconnectDependencies(new StubBridgeApiClient(), state),
                             ConnectTransportAsync: (_, _) => Task.FromResult<IReconnectableRemoteSessionTransport>(transport),
                             CreateSessionClient: (connectedTransport, sessionId) =>
                                 new FakeSessionClient(sessionId, connectedTransport),
                             CreateHeartbeatRuntime: () => heartbeatRuntime)))
        {
            await runtime.StartAsync();
            Assert.Equal(1, heartbeatRuntime.StartCalls);
            Assert.False(heartbeatRuntime.Disposed);
        }

        Assert.True(heartbeatRuntime.Disposed);
    }

    [Fact]
    public async Task TransportClose_Reconnects_InPlace_Without_Replaying_Initial_History()
    {
        ResetRegistry();
        var state = CreateState();
        var api = new StubBridgeApiClient
        {
            RegisterResponse = new BridgeRegisterEnvironmentResponse("env_123", "secret_2"),
            ReconnectErrors = new Queue<Exception?>([null])
        };
        var transports = new Queue<FakeReconnectableTransport>(
        [
            new FakeReconnectableTransport(sequenceNum: 77),
            new FakeReconnectableTransport(sequenceNum: 88)
        ]);
        var connectedClients = new List<FakeSessionClient>();
        var connectedTwice = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var runtime = new RemoteSessionTransportReconnectRuntime(
            new RemoteSessionTransportReconnectRuntimeDependencies(
                ReconnectDependencies: CreateReconnectDependencies(api, state),
                ConnectTransportAsync: (_, _) =>
                {
                    var next = transports.Count > 0
                        ? transports.Dequeue()
                        : throw new InvalidOperationException("Expected another transport.");
                    return Task.FromResult<IReconnectableRemoteSessionTransport>(next);
                },
                CreateSessionClient: (connectedTransport, sessionId) =>
                {
                    var client = new FakeSessionClient(sessionId, connectedTransport);
                    connectedClients.Add(client);
                    if (connectedClients.Count == 2)
                    {
                        connectedTwice.TrySetResult();
                    }

                    return client;
                },
                InitialMessages:
                [
                    new RemoteSessionOutboundMessage(
                        "user",
                        "uuid_1",
                        new JsonObject
                        {
                            ["type"] = "user",
                            ["uuid"] = "uuid_1"
                        })
                ]));

        await runtime.StartAsync();
        var firstTransport = (FakeReconnectableTransport)state.Transport!;
        firstTransport.RaisePermanentClose(4091);
        await connectedTwice.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(["session_123"], api.ReconnectAttempts);
        Assert.Equal("env_123", state.EnvironmentId);
        Assert.Equal("secret_2", state.EnvironmentSecret);
        Assert.Equal("session_123", state.CurrentSessionId);
        Assert.Equal(77, state.LastTransportSequenceNum);
        Assert.NotNull(state.Transport);
        Assert.NotSame(firstTransport, state.Transport);
        Assert.Equal(2, connectedClients.Count);
        var latestClient = connectedClients[^1];
        Assert.Equal("session_123", latestClient!.BridgeSessionId);
        Assert.Equal(0, latestClient.ConnectedPayloads.Count(payload => payload is not null));
    }

    [Fact]
    public async Task TransportClose_Recreates_Session_And_Replays_Initial_History_When_InPlace_Reconnect_Fails()
    {
        ResetRegistry();
        var state = CreateState();
        var api = new StubBridgeApiClient
        {
            RegisterResponse = new BridgeRegisterEnvironmentResponse("env_456", "secret_2"),
            ReconnectError = new InvalidOperationException("missing")
        };
        var pointerWrites = new List<(string SessionId, string EnvironmentId)>();
        var archivedSessions = new List<string>();
        var publishedIds = new List<string?>();
        var transports = new Queue<FakeReconnectableTransport>(
        [
            new FakeReconnectableTransport(sequenceNum: 21),
            new FakeReconnectableTransport(sequenceNum: 22)
        ]);
        var clients = new List<FakeSessionClient>();
        var connectedTwice = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var initialMessages = new[]
        {
            new RemoteSessionOutboundMessage(
                "user",
                "uuid_1",
                new JsonObject
                {
                    ["type"] = "user",
                    ["uuid"] = "uuid_1"
                })
        };

        await using var runtime = new RemoteSessionTransportReconnectRuntime(
            new RemoteSessionTransportReconnectRuntimeDependencies(
                ReconnectDependencies: CreateReconnectDependencies(
                    api,
                    state,
                    createSessionAsync: (_, _, _) => Task.FromResult<string?>("session_456"),
                    archiveSessionAsync: (sessionId, _) =>
                    {
                        archivedSessions.Add(sessionId);
                        return Task.CompletedTask;
                    },
                    writeBridgePointerAsync: (sessionId, environmentId, _) =>
                    {
                        pointerWrites.Add((sessionId, environmentId));
                        return Task.CompletedTask;
                    },
                    publishSessionBridgeIdAsync: sessionId =>
                    {
                        publishedIds.Add(sessionId);
                        return Task.CompletedTask;
                    }),
                ConnectTransportAsync: (_, _) =>
                {
                    var next = transports.Count > 0
                        ? transports.Dequeue()
                        : throw new InvalidOperationException("Expected another transport.");
                    return Task.FromResult<IReconnectableRemoteSessionTransport>(next);
                },
                CreateSessionClient: (connectedTransport, sessionId) =>
                {
                    var client = new FakeSessionClient(sessionId, connectedTransport);
                    clients.Add(client);
                    if (clients.Count == 2)
                    {
                        connectedTwice.TrySetResult();
                    }

                    return client;
                },
                InitialMessages: initialMessages));

        await runtime.StartAsync();
        var firstTransport = (FakeReconnectableTransport)state.Transport!;
        firstTransport.RaisePermanentClose(4091);
        await connectedTwice.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(["session_123"], archivedSessions);
        Assert.Equal([("session_456", "env_456")], pointerWrites);
        Assert.Equal(["session_456"], publishedIds);
        Assert.Equal("env_456", state.EnvironmentId);
        Assert.Equal("secret_2", state.EnvironmentSecret);
        Assert.Equal("session_456", state.CurrentSessionId);
        Assert.Equal(0, state.LastTransportSequenceNum);
        var latestClient = clients[^1];
        Assert.Equal("session_456", latestClient!.BridgeSessionId);
        Assert.Same(initialMessages, latestClient.ConnectedPayloads.Single());
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
            LastTransportSequenceNum = 0
        };
    }

    private static BridgeTransportReconnectDependencies CreateReconnectDependencies(
        StubBridgeApiClient api,
        BridgeTransportReconnectState state,
        Func<string, string?, CancellationToken, Task<string?>>? createSessionAsync = null,
        Func<string, CancellationToken, Task>? archiveSessionAsync = null,
        Func<string, string, CancellationToken, Task>? writeBridgePointerAsync = null,
        Func<string?, Task>? publishSessionBridgeIdAsync = null)
    {
        return new BridgeTransportReconnectDependencies(
            Api: api,
            State: state,
            WakePollLoop: () => { },
            DropFlushGate: () => 0,
            IsPollAborted: () => false,
            GetCurrentTitle: () => "Title",
            CreateSessionAsync: createSessionAsync ?? ((_, _, _) => Task.FromResult<string?>("session_456")),
            ArchiveSessionAsync: archiveSessionAsync ?? ((_, _) => Task.CompletedTask),
            WriteBridgePointerAsync: writeBridgePointerAsync ?? ((_, _, _) => Task.CompletedTask),
            ClearRecentInboundUuids: () => { },
            PublishSessionBridgeIdAsync: publishSessionBridgeIdAsync);
    }

    private static void ResetRegistry()
    {
        ReplBridgeHandleRegistry.SetSessionBridgeIdPublisher(null);
        ReplBridgeHandleRegistry.SetReplBridgeHandle(null);
    }

    private sealed class FakeSessionClient(string sessionId, IReconnectableRemoteSessionTransport transport) : IRemoteBridgeSessionClient
    {
        public List<IReadOnlyList<RemoteSessionOutboundMessage>?> ConnectedPayloads { get; } = [];

        public string BridgeSessionId { get; } = sessionId;

        public IReconnectableRemoteSessionTransport Transport { get; } = transport;

        public Task OnTransportConnectedAsync(
            IReadOnlyList<RemoteSessionOutboundMessage>? initialMessages,
            CancellationToken cancellationToken = default)
        {
            ConnectedPayloads.Add(initialMessages);
            return Task.CompletedTask;
        }

        public void HandleIngressData(string data)
        {
        }
    }

    private sealed class FakeReconnectableTransport(long sequenceNum = 0) : IReconnectableRemoteSessionTransport
    {
        public event Action<string>? DataReceived;

        public event Action<int?>? PermanentlyClosed;

        public List<JsonObject> WrittenMessages { get; } = [];

        public List<IReadOnlyList<JsonObject>> WrittenBatches { get; } = [];

        public List<string> ReportedStates { get; } = [];

        public bool Closed { get; private set; }

        public long GetLastSequenceNum() => sequenceNum;

        public Task WriteAsync(JsonObject message, CancellationToken cancellationToken = default)
        {
            WrittenMessages.Add(message.DeepClone()!.AsObject());
            return Task.CompletedTask;
        }

        public Task WriteBatchAsync(IReadOnlyList<JsonObject> messages, CancellationToken cancellationToken = default)
        {
            WrittenBatches.Add(messages.Select(message => message.DeepClone()!.AsObject()).ToArray());
            return Task.CompletedTask;
        }

        public void ReportState(string state)
        {
            ReportedStates.Add(state);
        }

        public void Close()
        {
            Closed = true;
        }

        public void RaisePermanentClose(int? closeCode)
        {
            PermanentlyClosed?.Invoke(closeCode);
        }

        public void RaiseData(string data)
        {
            DataReceived?.Invoke(data);
        }

        public ValueTask DisposeAsync()
        {
            Closed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeHeartbeatRuntime : IRemoteSessionHeartbeatRuntime
    {
        public int StartCalls { get; private set; }

        public bool Disposed { get; private set; }

        public void Start()
        {
            StartCalls++;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubBridgeApiClient : IBridgeApiClient
    {
        public BridgeRegisterEnvironmentResponse RegisterResponse { get; init; } = new("env_123", "secret_1");
        public Exception? ReconnectError { get; init; }
        public Queue<Exception?> ReconnectErrors { get; init; } = [];
        public List<string> ReconnectAttempts { get; } = [];

        public Task<BridgeRegisterEnvironmentResponse> RegisterBridgeEnvironmentAsync(BridgeConfig config, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(RegisterResponse);
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
}
