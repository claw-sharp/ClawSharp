using System.Text.Json.Nodes;
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class RemoteSessionDirectConnectRuntimeTests
{
    [Fact]
    public async Task StartAsync_Bootstraps_Session_And_Flushes_Initial_History()
    {
        var transport = new FakeReconnectableTransport();

        await using var runtime = new RemoteSessionDirectConnectRuntime(
            CreateDependencies(
                connectTransportAsync: (_, _, _, _) =>
                    Task.FromResult<IReconnectableRemoteSessionTransport>(transport)));

        var info = await runtime.StartAsync(
            new RemoteSessionDirectConnectRequest(
                "https://api.example.com",
                "Review Session",
                InitialMessages:
                [
                    new RemoteSessionOutboundMessage(
                        "user",
                        "uuid-1",
                        new JsonObject
                        {
                            ["type"] = "user",
                            ["uuid"] = "uuid-1"
                        })
                ]));

        Assert.NotNull(info);
        Assert.Equal("cse_123", runtime.BridgeSessionId);
        Assert.Equal("connected", info!.ConnectionStatus);
        Assert.Single(transport.WrittenMessages);
        Assert.Equal("cse_123", transport.WrittenMessages[0]["session_id"]?.GetValue<string>());
    }

    [Fact]
    public async Task SendControlRequestAsync_Routes_To_Active_Transport()
    {
        var transport = new FakeReconnectableTransport();

        await using var runtime = new RemoteSessionDirectConnectRuntime(
            CreateDependencies(
                connectTransportAsync: (_, _, _, _) =>
                    Task.FromResult<IReconnectableRemoteSessionTransport>(transport)));

        await runtime.StartAsync(
            new RemoteSessionDirectConnectRequest(
                "https://api.example.com",
                "Review Session"));

        await runtime.SendControlRequestAsync(
            new JsonObject
            {
                ["type"] = "control_request",
                ["request_id"] = "req-1",
                ["request"] = new JsonObject
                {
                    ["subtype"] = "can_use_tool"
                }
            });

        Assert.Single(transport.WrittenMessages);
        Assert.Equal("cse_123", transport.WrittenMessages[0]["session_id"]?.GetValue<string>());
    }

    [Fact]
    public async Task Transport401_Rebuilds_Transport_With_Fresh_Credentials()
    {
        var firstTransport = new FakeReconnectableTransport();
        var secondTransport = new FakeReconnectableTransport();
        var fetchCount = 0;
        var reconnectReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var runtime = new RemoteSessionDirectConnectRuntime(
            CreateDependencies(
                fetchRemoteCredentialsAsync: (_, _, _, _, _, _) =>
                {
                    fetchCount++;
                    return Task.FromResult<RemoteCredentials?>(
                        fetchCount == 1
                            ? new RemoteCredentials("jwt-1", "https://worker.example.com", 3600, 12)
                            : new RemoteCredentials("jwt-2", "https://worker.example.com", 3600, 13));
                },
                connectTransportAsync: (_, credentials, _, _) =>
                {
                    if (credentials.WorkerJwt == "jwt-2")
                    {
                        reconnectReady.TrySetResult();
                        return Task.FromResult<IReconnectableRemoteSessionTransport>(secondTransport);
                    }

                    return Task.FromResult<IReconnectableRemoteSessionTransport>(firstTransport);
                }));

        await runtime.StartAsync(
            new RemoteSessionDirectConnectRequest(
                "https://api.example.com",
                "Review Session"));

        firstTransport.RaisePermanentClose(401);
        await reconnectReady.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await runtime.SendControlRequestAsync(
            new JsonObject
            {
                ["type"] = "control_request",
                ["request_id"] = "req-1",
                ["request"] = new JsonObject
                {
                    ["subtype"] = "can_use_tool"
                }
            });

        Assert.Equal(2, fetchCount);
        Assert.Single(secondTransport.WrittenMessages);
        Assert.Equal("cse_123", secondTransport.WrittenMessages[0]["session_id"]?.GetValue<string>());
    }

    [Fact]
    public async Task DisposeAsync_Tears_Down_And_Archives_Session()
    {
        var archivedSessions = new List<(string SessionId, int TimeoutMs)>();
        var transport = new FakeReconnectableTransport();
        var runtime = new RemoteSessionDirectConnectRuntime(
            CreateDependencies(
                archiveSessionAsync: (sessionId, timeoutMs, _) =>
                {
                    archivedSessions.Add((sessionId, timeoutMs));
                    return Task.CompletedTask;
                },
                connectTransportAsync: (_, _, _, _) =>
                    Task.FromResult<IReconnectableRemoteSessionTransport>(transport)));

        await runtime.StartAsync(
            new RemoteSessionDirectConnectRequest(
                "https://api.example.com",
                "Review Session"));
        await runtime.DisposeAsync();

        Assert.Equal([("cse_123", EnvLessBridgeConfig.Default.TeardownArchiveTimeoutMs)], archivedSessions);
        Assert.True(transport.Closed);
    }

    private static RemoteSessionDirectConnectDependencies CreateDependencies(
        Func<string, string, string, int, IReadOnlyList<string>?, CancellationToken, Task<string?>>? createCodeSessionAsync = null,
        Func<string, string, string, int, string?, CancellationToken, Task<RemoteCredentials?>>? fetchRemoteCredentialsAsync = null,
        Func<string, int, CancellationToken, Task>? archiveSessionAsync = null,
        Func<string, RemoteCredentials, Action<string>?, CancellationToken, Task<IReconnectableRemoteSessionTransport>>? connectTransportAsync = null)
    {
        return new RemoteSessionDirectConnectDependencies(
            BootstrapDependencies: new RemoteSessionBootstrapDependencies(
                IsEnvLessBridgeEnabled: () => true,
                GetEnvLessBridgeConfigAsync: _ => Task.FromResult(EnvLessBridgeConfig.Default),
                CheckEnvLessBridgeMinVersionAsync: _ => Task.FromResult<string?>(null),
                GetAccessToken: () => "oauth-token",
                CreateCodeSessionAsync: createCodeSessionAsync ?? ((_, _, _, _, _, _) => Task.FromResult<string?>("cse_123")),
                FetchRemoteCredentialsAsync: fetchRemoteCredentialsAsync ?? ((_, _, _, _, _, _) =>
                    Task.FromResult<RemoteCredentials?>(new RemoteCredentials("jwt-1", "https://worker.example.com", 3600, 12))),
                ArchiveSessionAsync: archiveSessionAsync ?? ((_, _, _) => Task.CompletedTask)),
            ConnectTransportAsync: connectTransportAsync ?? ((_, _, _, _) =>
                Task.FromResult<IReconnectableRemoteSessionTransport>(new FakeReconnectableTransport())));
    }

    private sealed class FakeReconnectableTransport : IReconnectableRemoteSessionTransport
    {
        public event Action<string>? DataReceived;

        public event Action<int?>? PermanentlyClosed;

        public List<JsonObject> WrittenMessages { get; } = [];

        public bool Closed { get; private set; }

        public long GetLastSequenceNum() => 0;

        public Task WriteAsync(JsonObject message, CancellationToken cancellationToken = default)
        {
            WrittenMessages.Add(message.DeepClone()!.AsObject());
            return Task.CompletedTask;
        }

        public Task WriteBatchAsync(IReadOnlyList<JsonObject> messages, CancellationToken cancellationToken = default)
        {
            foreach (var message in messages)
            {
                WrittenMessages.Add(message.DeepClone()!.AsObject());
            }

            return Task.CompletedTask;
        }

        public void ReportState(string state)
        {
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
}
