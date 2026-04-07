using System.Text.Json.Nodes;
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgeControlEventRouterTests
{
    [Fact]
    public async Task HandleServerControlRequestAsync_Initialize_Writes_Success_Response()
    {
        var transport = new StubTransport();
        var debugMessages = new List<string>();

        await BridgeControlEventRouter.HandleServerControlRequestAsync(
            CreateControlRequest("req_123", "initialize"),
            new BridgeServerControlRequestHandlers(
                transport,
                "session_123",
                OnDebug: debugMessages.Add));

        var response = Assert.Single(transport.Messages);
        Assert.Equal("control_response", response["type"]?.GetValue<string>());
        Assert.Equal("success", response["response"]?["subtype"]?.GetValue<string>());
        Assert.Equal("req_123", response["response"]?["request_id"]?.GetValue<string>());
        Assert.Equal("session_123", response["session_id"]?.GetValue<string>());
        Assert.Equal(Environment.ProcessId, response["response"]?["response"]?["pid"]?.GetValue<int>());
        Assert.Contains(debugMessages, m => m.Contains("Sent control_response for initialize", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleServerControlRequestAsync_SetPermissionMode_Uses_Error_Verdict()
    {
        var transport = new StubTransport();

        await BridgeControlEventRouter.HandleServerControlRequestAsync(
            CreateControlRequest("req_123", "set_permission_mode", new JsonObject { ["mode"] = "auto" }),
            new BridgeServerControlRequestHandlers(
                transport,
                "session_123",
                OnSetPermissionMode: _ => new BridgePermissionModeVerdict(false, "blocked")));

        var response = Assert.Single(transport.Messages);
        Assert.Equal("error", response["response"]?["subtype"]?.GetValue<string>());
        Assert.Equal("blocked", response["response"]?["error"]?.GetValue<string>());
    }

    [Fact]
    public async Task HandleServerControlRequestAsync_OutboundOnly_Rejects_Mutable_Request()
    {
        var transport = new StubTransport();

        await BridgeControlEventRouter.HandleServerControlRequestAsync(
            CreateControlRequest("req_123", "interrupt"),
            new BridgeServerControlRequestHandlers(
                transport,
                "session_123",
                OutboundOnly: true));

        var response = Assert.Single(transport.Messages);
        Assert.Equal("error", response["response"]?["subtype"]?.GetValue<string>());
        Assert.Equal(
            "This session is outbound-only. Enable Remote Control locally to allow inbound control.",
            response["response"]?["error"]?.GetValue<string>());
    }

    [Fact]
    public async Task SendControlRequestAsync_RemoteBridge_Drops_During_Auth_Recovery()
    {
        var transport = new StubTransport();
        var debugMessages = new List<string>();

        await BridgeControlEventRouter.SendControlRequestAsync(
            CreateControlRequest("req_123", "can_use_tool"),
            new BridgeOutboundControlEventHandlers(
                transport,
                "session_123",
                AuthRecoveryInFlight: true,
                IsRemoteBridge: true,
                OnDebug: debugMessages.Add));

        Assert.Empty(transport.Messages);
        Assert.Contains(debugMessages, m => m.Contains("Dropping control_request during 401 recovery: req_123", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SendControlRequestAsync_RemoteBridge_Reports_RequiresAction_For_CanUseTool()
    {
        var transport = new StubTransport();
        var states = new List<string>();

        await BridgeControlEventRouter.SendControlRequestAsync(
            CreateControlRequest("req_123", "can_use_tool"),
            new BridgeOutboundControlEventHandlers(
                transport,
                "session_123",
                IsRemoteBridge: true,
                ReportState: states.Add));

        var request = Assert.Single(transport.Messages);
        Assert.Equal("session_123", request["session_id"]?.GetValue<string>());
        Assert.Equal(["requires_action"], states);
    }

    [Fact]
    public async Task SendControlResponseAsync_RemoteBridge_Reports_Running()
    {
        var transport = new StubTransport();
        var states = new List<string>();

        await BridgeControlEventRouter.SendControlResponseAsync(
            new JsonObject
            {
                ["type"] = "control_response",
                ["response"] = new JsonObject
                {
                    ["subtype"] = "success",
                    ["request_id"] = "req_123"
                }
            },
            new BridgeOutboundControlEventHandlers(
                transport,
                "session_123",
                IsRemoteBridge: true,
                ReportState: states.Add));

        Assert.Single(transport.Messages);
        Assert.Equal(["running"], states);
    }

    [Fact]
    public async Task SendControlCancelRequestAsync_RemoteBridge_Reports_Running()
    {
        var transport = new StubTransport();
        var states = new List<string>();

        await BridgeControlEventRouter.SendControlCancelRequestAsync(
            "req_123",
            new BridgeOutboundControlEventHandlers(
                transport,
                "session_123",
                IsRemoteBridge: true,
                ReportState: states.Add));

        var message = Assert.Single(transport.Messages);
        Assert.Equal("control_cancel_request", message["type"]?.GetValue<string>());
        Assert.Equal(["running"], states);
    }

    [Fact]
    public async Task SendResultAsync_RemoteBridge_Reports_Idle()
    {
        var transport = new StubTransport();
        var states = new List<string>();

        await BridgeControlEventRouter.SendResultAsync(
            new BridgeOutboundControlEventHandlers(
                transport,
                "session_123",
                IsRemoteBridge: true,
                ReportState: states.Add));

        var message = Assert.Single(transport.Messages);
        Assert.Equal("result", message["type"]?.GetValue<string>());
        Assert.Equal("session_123", message["session_id"]?.GetValue<string>());
        Assert.Equal(["idle"], states);
    }

    private static JsonObject CreateControlRequest(string requestId, string subtype, JsonObject? request = null)
    {
        return new JsonObject
        {
            ["type"] = "control_request",
            ["request_id"] = requestId,
            ["request"] = request ?? new JsonObject
            {
                ["subtype"] = subtype
            }
        }.Also(obj =>
        {
            if (obj["request"] is JsonObject requestObject)
            {
                requestObject["subtype"] ??= subtype;
            }
        });
    }

    private sealed class StubTransport : IBridgeControlEventTransport
    {
        public List<JsonObject> Messages { get; } = [];

        public Task WriteAsync(JsonObject message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message.DeepClone()!.AsObject());
            return Task.CompletedTask;
        }
    }
}

internal static class BridgeControlEventRouterTestExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
