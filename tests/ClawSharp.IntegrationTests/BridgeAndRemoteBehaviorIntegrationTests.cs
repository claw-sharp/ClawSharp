using System.Text.Json.Nodes;
using ClawSharp.Bridge;

namespace ClawSharp.IntegrationTests;

public sealed class BridgeAndRemoteBehaviorIntegrationTests
{
    [Fact]
    public async Task Remote_Bridge_Bootstrap_And_Manager_Flow_Work_End_To_End()
    {
        var archivedSessions = new List<(string SessionId, int TimeoutMs)>();
        var states = new List<(string State, string? Detail)>();
        var transport = new RecordingRemoteSessionTransport();
        var bootstrapCoordinator = new RemoteSessionBootstrapCoordinator(
            new RemoteSessionBootstrapDependencies(
                IsEnvLessBridgeEnabled: () => true,
                GetEnvLessBridgeConfigAsync: _ => Task.FromResult(EnvLessBridgeConfig.Default),
                CheckEnvLessBridgeMinVersionAsync: _ => Task.FromResult<string?>(null),
                GetAccessToken: () => "oauth-token",
                CreateCodeSessionAsync: (_, _, _, _, _, _) => Task.FromResult<string?>("cse_123"),
                FetchRemoteCredentialsAsync: (_, _, _, _, _, _) =>
                    Task.FromResult<RemoteCredentials?>(new RemoteCredentials("worker-jwt", "https://worker.example.test", 3600, 12)),
                ArchiveSessionAsync: (sessionId, timeoutMs, _) =>
                {
                    archivedSessions.Add((sessionId, timeoutMs));
                    return Task.CompletedTask;
                },
                OnStateChange: (state, detail) => states.Add((state, detail))));

        var bootstrap = await bootstrapCoordinator.TryBootstrapDirectConnectAsync(
            new RemoteSessionBootstrapRequest("https://api.example.test", "Review Session"));

        Assert.NotNull(bootstrap);
        Assert.Equal("cse_123", bootstrap!.SessionId);
        Assert.Equal("worker-jwt", bootstrap.Credentials.WorkerJwt);

        var initialMessage = new RemoteSessionOutboundMessage(
            "user",
            "uuid-1",
            new JsonObject
            {
                ["type"] = "user",
                ["uuid"] = "uuid-1"
            },
            TitleMessage: new BridgeTitleMessage("user", StringContent: "hello"));
        var manager = new RemoteSessionManager(
            new RemoteSessionManagerDependencies(
                transport,
                bootstrap.SessionId,
                InitialHistoryCap: 200,
                ArchiveSessionAsync: (sessionId, _) =>
                {
                    archivedSessions.Add((sessionId, 0));
                    return Task.FromResult<int?>(204);
                },
                OnStateChange: (state, detail) => states.Add((state, detail))),
            uuidDedupBufferSize: 16,
            initialMessages: [initialMessage]);

        await manager.OnTransportConnectedAsync([initialMessage]);
        await manager.SendControlRequestAsync(
            new JsonObject
            {
                ["type"] = "control_request",
                ["request_id"] = "req-1",
                ["request"] = new JsonObject
                {
                    ["subtype"] = "can_use_tool"
                }
            });
        await manager.TeardownAsync();

        Assert.Equal(["running", "requires_action", "idle"], transport.ReportedStates);
        Assert.Single(transport.Batches);
        Assert.Equal(2, transport.Messages.Count);
        Assert.Contains(states, state => state.State == "connected");
        Assert.Contains(archivedSessions, item => item.SessionId == "cse_123");
        Assert.True(manager.TornDown);
        Assert.True(transport.Closed);
    }

    private sealed class RecordingRemoteSessionTransport : IRemoteSessionTransport
    {
        public List<JsonObject> Messages { get; } = [];
        public List<IReadOnlyList<JsonObject>> Batches { get; } = [];
        public List<string> ReportedStates { get; } = [];
        public bool Closed { get; private set; }

        public Task WriteAsync(JsonObject message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message.DeepClone()!.AsObject());
            return Task.CompletedTask;
        }

        public Task WriteBatchAsync(IReadOnlyList<JsonObject> messages, CancellationToken cancellationToken = default)
        {
            Batches.Add(messages.Select(message => message.DeepClone()!.AsObject()).ToArray());
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
    }
}
