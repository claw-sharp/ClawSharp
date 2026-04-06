using System.Text.Json.Nodes;
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class RemoteSessionManagerRuntimeTests
{
    [Fact]
    public async Task StartAsync_Creates_Manager_And_Routes_Outbound_Writes()
    {
        var state = CreateState();
        var transport = new FakeReconnectableTransport();
        var archivedSessions = new List<string>();

        await using var runtime = new RemoteSessionManagerRuntime(
            new RemoteSessionManagerRuntimeDependencies(
                ReconnectDependencies: CreateReconnectDependencies(new StubBridgeApiClient(), state),
                ConnectTransportAsync: (_, _) => Task.FromResult<IReconnectableRemoteSessionTransport>(transport),
                ArchiveSessionAsync: (sessionId, _) =>
                {
                    archivedSessions.Add(sessionId);
                    return Task.FromResult<int?>(204);
                },
                InitialHistoryCap: 200,
                UuidDedupBufferSize: 16));

        await runtime.StartAsync();
        runtime.WriteMessages(
        [
            new RemoteSessionOutboundMessage(
                "user",
                "uuid_1",
                new JsonObject
                {
                    ["type"] = "user",
                    ["uuid"] = "uuid_1"
                })
        ]);

        Assert.NotNull(runtime.CurrentManager);
        Assert.Equal("session_123", runtime.CurrentManager!.BridgeSessionId);
        Assert.Equal(["running"], transport.ReportedStates);
        Assert.Single(transport.WrittenBatches);
        Assert.Equal("session_123", transport.WrittenBatches[0][0]["session_id"]?.GetValue<string>());

        await runtime.TeardownAsync();
        Assert.Equal(["session_123"], archivedSessions);
    }

    [Fact]
    public async Task TransportClose_Recreates_Manager_And_Routes_Later_Writes_To_New_Session()
    {
        var state = CreateState();
        var api = new StubBridgeApiClient
        {
            RegisterResponse = new BridgeRegisterEnvironmentResponse("env_456", "secret_2"),
            ReconnectError = new InvalidOperationException("missing")
        };
        var states = new List<(string State, string? Detail)>();
        var archivedSessions = new List<string>();
        var transports = new Queue<FakeReconnectableTransport>(
        [
            new FakeReconnectableTransport(sequenceNum: 21),
            new FakeReconnectableTransport(sequenceNum: 22)
        ]);
        var reconnectReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var runtime = new RemoteSessionManagerRuntime(
            new RemoteSessionManagerRuntimeDependencies(
                ReconnectDependencies: CreateReconnectDependencies(
                    api,
                    state,
                    createSessionAsync: (_, _, _) => Task.FromResult<string?>("session_456"),
                    archiveSessionAsync: (sessionId, _) =>
                    {
                        archivedSessions.Add(sessionId);
                        return Task.CompletedTask;
                    },
                    writeBridgePointerAsync: (_, _, _) => Task.CompletedTask,
                    publishSessionBridgeIdAsync: _ => Task.CompletedTask),
                ConnectTransportAsync: (_, _) =>
                {
                    var next = transports.Count > 0
                        ? transports.Dequeue()
                        : throw new InvalidOperationException("Expected another transport.");
                    return Task.FromResult<IReconnectableRemoteSessionTransport>(next);
                },
                ArchiveSessionAsync: (sessionId, _) => Task.FromResult<int?>(204),
                InitialHistoryCap: 200,
                UuidDedupBufferSize: 16,
                OnStateChange: (status, detail) =>
                {
                    states.Add((status, detail));
                    if (states.Count(s => s.State == "connected") == 2)
                    {
                        reconnectReady.TrySetResult();
                    }
                }));

        await runtime.StartAsync();
        var firstTransport = (FakeReconnectableTransport)state.Transport!;
        firstTransport.RaisePermanentClose(4091);
        await reconnectReady.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.NotNull(runtime.CurrentManager);
        Assert.Equal("session_456", runtime.CurrentManager!.BridgeSessionId);
        Assert.Equal(["session_123"], archivedSessions);

        await runtime.SendControlRequestAsync(
            new JsonObject
            {
                ["type"] = "control_request",
                ["request_id"] = "req_1",
                ["request"] = new JsonObject
                {
                    ["subtype"] = "can_use_tool"
                }
            });

        var secondTransport = transports.Count == 0
            ? (FakeReconnectableTransport)state.Transport!
            : throw new InvalidOperationException("Unexpected unused transport.");
        Assert.Single(secondTransport.WrittenMessages);
        Assert.Equal("session_456", secondTransport.WrittenMessages[0]["session_id"]?.GetValue<string>());
    }

    [Fact]
    public async Task DisposeAsync_Tears_Down_Current_Manager_And_Clears_CurrentManager()
    {
        var state = CreateState();
        var transport = new FakeReconnectableTransport();
        var archivedSessions = new List<string>();
        var runtime = new RemoteSessionManagerRuntime(
            new RemoteSessionManagerRuntimeDependencies(
                ReconnectDependencies: CreateReconnectDependencies(new StubBridgeApiClient(), state),
                ConnectTransportAsync: (_, _) => Task.FromResult<IReconnectableRemoteSessionTransport>(transport),
                ArchiveSessionAsync: (sessionId, _) =>
                {
                    archivedSessions.Add(sessionId);
                    return Task.FromResult<int?>(204);
                },
                InitialHistoryCap: 200,
                UuidDedupBufferSize: 16));

        await runtime.StartAsync();
        await runtime.DisposeAsync();

        Assert.Equal(["idle"], transport.ReportedStates);
        Assert.Equal(["session_123"], archivedSessions);
        Assert.True(transport.Closed);
        Assert.Null(runtime.CurrentManager);
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

    private sealed class StubBridgeApiClient : IBridgeApiClient
    {
        public BridgeRegisterEnvironmentResponse RegisterResponse { get; init; } = new("env_123", "secret_1");
        public Exception? ReconnectError { get; init; }

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
