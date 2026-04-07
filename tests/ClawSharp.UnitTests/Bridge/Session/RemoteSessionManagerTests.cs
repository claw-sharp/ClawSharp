using System.Text.Json.Nodes;
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class RemoteSessionManagerTests
{
    [Fact]
    public async Task OnTransportConnectedAsync_Flushes_Initial_History_And_Transitions_To_Connected()
    {
        var transport = new StubTransport();
        var states = new List<(string State, string? Detail)>();
        var manager = new RemoteSessionManager(
            new RemoteSessionManagerDependencies(
                transport,
                "cse_123",
                200,
                (_, _) => Task.FromResult<int?>(200),
                OnStateChange: (state, detail) => states.Add((state, detail)),
                OnDebug: _ => { }),
            uuidDedupBufferSize: 16,
            initialMessages:
            [
                CreateOutboundMessage("user", "u1"),
                CreateOutboundMessage("assistant", "a1")
            ]);

        await manager.OnTransportConnectedAsync(
            [
                CreateOutboundMessage("user", "u1"),
                CreateOutboundMessage("assistant", "a1")
            ]);

        Assert.Equal(["running"], transport.ReportedStates);
        Assert.Single(transport.Batches);
        Assert.Equal(2, transport.Batches[0].Count);
        Assert.Contains(states, s => s.State == "connected");
    }

    [Fact]
    public void WriteMessages_Queues_During_Flush_And_Drains_After_History()
    {
        var transport = new StubTransport();
        var manager = new RemoteSessionManager(
            new RemoteSessionManagerDependencies(
                transport,
                "cse_123",
                200,
                (_, _) => Task.FromResult<int?>(200),
                OnDebug: _ => { }),
            uuidDedupBufferSize: 16,
            initialMessages:
            [
                CreateOutboundMessage("user", "init1")
            ]);

        manager.WriteMessages(
            [
                CreateOutboundMessage("user", "queued1")
            ]);

        Assert.Empty(transport.Batches);

        manager.OnTransportConnectedAsync(
            [
                CreateOutboundMessage("user", "init1")
            ]).GetAwaiter().GetResult();

        Assert.Equal(2, transport.Batches.Count);
        Assert.Single(transport.Batches[1]);
        Assert.Equal("queued1", transport.Batches[1][0]["uuid"]?.GetValue<string>());
    }

    [Fact]
    public async Task SendControlRequestAsync_Remote_Manager_Uses_RequiresAction_State()
    {
        var transport = new StubTransport();
        var manager = new RemoteSessionManager(
            new RemoteSessionManagerDependencies(
                transport,
                "cse_123",
                200,
                (_, _) => Task.FromResult<int?>(200)),
            uuidDedupBufferSize: 16);

        await manager.SendControlRequestAsync(
            new JsonObject
            {
                ["type"] = "control_request",
                ["request_id"] = "req_1",
                ["request"] = new JsonObject
                {
                    ["subtype"] = "can_use_tool"
                }
            });

        Assert.Equal(["requires_action"], transport.ReportedStates);
        Assert.Single(transport.Messages);
    }

    [Fact]
    public async Task TeardownAsync_Sends_Result_Archives_And_Closes()
    {
        var transport = new StubTransport();
        var archived = new List<string>();
        var manager = new RemoteSessionManager(
            new RemoteSessionManagerDependencies(
                transport,
                "cse_123",
                200,
                (sessionId, _) =>
                {
                    archived.Add(sessionId);
                    return Task.FromResult<int?>(204);
                },
                OnDebug: _ => { }),
            uuidDedupBufferSize: 16);

        await manager.TeardownAsync();

        Assert.True(manager.TornDown);
        Assert.Equal(["idle"], transport.ReportedStates);
        Assert.Single(transport.Messages);
        Assert.Equal("result", transport.Messages[0]["type"]?.GetValue<string>());
        Assert.Equal(["cse_123"], archived);
        Assert.True(transport.Closed);
    }

    [Fact]
    public void OnTransportClosed_Sets_Failed_State_For_Non401()
    {
        var transport = new StubTransport();
        var states = new List<(string State, string? Detail)>();
        var manager = new RemoteSessionManager(
            new RemoteSessionManagerDependencies(
                transport,
                "cse_123",
                200,
                (_, _) => Task.FromResult<int?>(200),
                OnStateChange: (state, detail) => states.Add((state, detail))),
            uuidDedupBufferSize: 16);

        manager.OnTransportClosed(4091);

        Assert.Contains(states, s => s.State == "failed" && s.Detail == "Transport closed (code 4091)");
    }

    private static RemoteSessionOutboundMessage CreateOutboundMessage(string type, string uuid)
    {
        return new RemoteSessionOutboundMessage(
            type,
            uuid,
            new JsonObject
            {
                ["type"] = type,
                ["uuid"] = uuid
            },
            TitleMessage: new BridgeTitleMessage(type, StringContent: "hello"));
    }

    private sealed class StubTransport : IRemoteSessionTransport
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
