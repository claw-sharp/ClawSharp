using ClawSharp.Bridge;
using System.Text.Json.Nodes;

namespace ClawSharp.UnitTests;

public sealed class BridgeMessagingUtilitiesTests
{
    [Fact]
    public void IsSdkMessage_Only_Checks_For_String_Type_Discriminant()
    {
        Assert.True(BridgeMessagingUtilities.IsSdkMessage(new { Type = "user" }));
        Assert.True(BridgeMessagingUtilities.IsSdkMessage(new Dictionary<string, object?> { ["type"] = "assistant" }));
        Assert.False(BridgeMessagingUtilities.IsSdkMessage(new { Type = 123 }));
        Assert.False(BridgeMessagingUtilities.IsSdkMessage(null));
    }

    [Fact]
    public void IsSdkControlResponse_Matches_Ts_Response_Guard()
    {
        Assert.True(BridgeMessagingUtilities.IsSdkControlResponse(
            new Dictionary<string, object?> { ["type"] = "control_response", ["response"] = new object() }));
        Assert.False(BridgeMessagingUtilities.IsSdkControlResponse(
            new Dictionary<string, object?> { ["type"] = "control_response" }));
        Assert.False(BridgeMessagingUtilities.IsSdkControlResponse(
            new Dictionary<string, object?> { ["type"] = "user", ["response"] = new object() }));
    }

    [Fact]
    public void IsSdkControlRequest_Matches_Ts_Request_Guard()
    {
        Assert.True(BridgeMessagingUtilities.IsSdkControlRequest(
            new BridgeControlRequestMessage("control_request", "req-1", new { subtype = "initialize" })));
        Assert.False(BridgeMessagingUtilities.IsSdkControlRequest(
            new { Type = "control_request", RequestId = "req-1" }));
        Assert.False(BridgeMessagingUtilities.IsSdkControlRequest(
            new { Type = "assistant", RequestId = "req-1", Request = new object() }));
    }

    [Fact]
    public void NormalizeControlMessageKeys_Moves_CamelCase_RequestId_Without_Overwriting_SnakeCase()
    {
        var request = JsonNode.Parse("""{"type":"control_request","requestId":"req-1","request":{"subtype":"initialize"}}""");
        var response = JsonNode.Parse("""{"type":"control_response","response":{"requestId":"req-2"}}""");
        var snakeWins = JsonNode.Parse("""{"type":"control_request","request_id":"req-1","requestId":"req-2","request":{"subtype":"initialize"}}""");

        var normalizedRequest = BridgeMessagingUtilities.NormalizeControlMessageKeys(request);
        var normalizedResponse = BridgeMessagingUtilities.NormalizeControlMessageKeys(response);
        var normalizedSnakeWins = BridgeMessagingUtilities.NormalizeControlMessageKeys(snakeWins);

        Assert.Equal("req-1", normalizedRequest!["request_id"]!.GetValue<string>());
        Assert.Null(normalizedRequest["requestId"]);
        Assert.Equal("req-2", normalizedResponse!["response"]!["request_id"]!.GetValue<string>());
        Assert.Null(normalizedResponse["response"]!["requestId"]);
        Assert.Equal("req-1", normalizedSnakeWins!["request_id"]!.GetValue<string>());
        Assert.Equal("req-2", normalizedSnakeWins["requestId"]!.GetValue<string>());
    }

    [Fact]
    public void IsEligibleBridgeMessage_Follows_Ts_Type_And_Virtual_Rules()
    {
        Assert.True(BridgeMessagingUtilities.IsEligibleBridgeMessage(new BridgeMessageEnvelope("user")));
        Assert.True(BridgeMessagingUtilities.IsEligibleBridgeMessage(new BridgeMessageEnvelope("assistant")));
        Assert.True(BridgeMessagingUtilities.IsEligibleBridgeMessage(new BridgeMessageEnvelope("system", Subtype: "local_command")));
        Assert.False(BridgeMessagingUtilities.IsEligibleBridgeMessage(new BridgeMessageEnvelope("system", Subtype: "other")));
        Assert.False(BridgeMessagingUtilities.IsEligibleBridgeMessage(new BridgeMessageEnvelope("assistant", IsVirtual: true)));
    }

    [Fact]
    public void HandleIngressMessage_Routes_Control_And_User_Messages_And_Dedups_Errors()
    {
        List<string> debug = [];
        List<JsonObject> permissionResponses = [];
        List<JsonObject> controlRequests = [];
        List<JsonObject> inboundMessages = [];
        var recentPosted = new BoundedUuidSet(8);
        var recentInbound = new BoundedUuidSet(8);
        var bridgeMessageCount = 0;

        recentPosted.Add("echo-1");
        recentInbound.Add("dup-1");

        BridgeMessagingUtilities.HandleIngressMessage(
            """{"type":"control_response","response":{"requestId":"req-1"}}""",
            recentPosted,
            recentInbound,
            message =>
            {
                inboundMessages.Add(message);
                return Task.CompletedTask;
            },
            permissionResponses.Add,
            controlRequests.Add,
            debug.Add,
            () => bridgeMessageCount++);
        BridgeMessagingUtilities.HandleIngressMessage(
            """{"type":"control_request","requestId":"req-2","request":{"subtype":"initialize"}}""",
            recentPosted,
            recentInbound,
            message =>
            {
                inboundMessages.Add(message);
                return Task.CompletedTask;
            },
            permissionResponses.Add,
            controlRequests.Add,
            debug.Add,
            () => bridgeMessageCount++);
        BridgeMessagingUtilities.HandleIngressMessage(
            """{"type":"user","uuid":"echo-1"}""",
            recentPosted,
            recentInbound,
            message =>
            {
                inboundMessages.Add(message);
                return Task.CompletedTask;
            },
            permissionResponses.Add,
            controlRequests.Add,
            debug.Add,
            () => bridgeMessageCount++);
        BridgeMessagingUtilities.HandleIngressMessage(
            """{"type":"user","uuid":"dup-1"}""",
            recentPosted,
            recentInbound,
            message =>
            {
                inboundMessages.Add(message);
                return Task.CompletedTask;
            },
            permissionResponses.Add,
            controlRequests.Add,
            debug.Add,
            () => bridgeMessageCount++);
        BridgeMessagingUtilities.HandleIngressMessage(
            """{"type":"user","uuid":"fresh-1"}""",
            recentPosted,
            recentInbound,
            message =>
            {
                inboundMessages.Add(message);
                return Task.CompletedTask;
            },
            permissionResponses.Add,
            controlRequests.Add,
            debug.Add,
            () => bridgeMessageCount++);
        BridgeMessagingUtilities.HandleIngressMessage(
            """{"type":"assistant"}""",
            recentPosted,
            recentInbound,
            message =>
            {
                inboundMessages.Add(message);
                return Task.CompletedTask;
            },
            permissionResponses.Add,
            controlRequests.Add,
            debug.Add,
            () => bridgeMessageCount++);
        BridgeMessagingUtilities.HandleIngressMessage(
            "not json",
            recentPosted,
            recentInbound,
            message =>
            {
                inboundMessages.Add(message);
                return Task.CompletedTask;
            },
            permissionResponses.Add,
            controlRequests.Add,
            debug.Add,
            () => bridgeMessageCount++);

        Assert.Single(permissionResponses);
        Assert.Equal("req-1", permissionResponses[0]["response"]!["request_id"]!.GetValue<string>());
        Assert.Single(controlRequests);
        Assert.Equal("req-2", controlRequests[0]["request_id"]!.GetValue<string>());
        Assert.Single(inboundMessages);
        Assert.Equal("fresh-1", inboundMessages[0]["uuid"]!.GetValue<string>());
        Assert.True(recentInbound.Contains("fresh-1"));
        Assert.Equal(1, bridgeMessageCount);
        Assert.Contains(debug, line => line.Contains("Ignoring echo", StringComparison.Ordinal));
        Assert.Contains(debug, line => line.Contains("Ignoring re-delivered inbound", StringComparison.Ordinal));
        Assert.Contains(debug, line => line.Contains("Ignoring non-user inbound message", StringComparison.Ordinal));
        Assert.Contains(debug, line => line.Contains("Failed to parse ingress message", StringComparison.Ordinal));
    }

    [Fact]
    public void ExtractTitleText_Follows_Ts_User_Meta_Origin_And_Display_Tag_Rules()
    {
        Assert.Null(BridgeMessagingUtilities.ExtractTitleText(new BridgeTitleMessage("assistant", StringContent: "nope")));
        Assert.Null(BridgeMessagingUtilities.ExtractTitleText(new BridgeTitleMessage("user", IsMeta: true, StringContent: "nope")));
        Assert.Null(BridgeMessagingUtilities.ExtractTitleText(new BridgeTitleMessage("user", Origin: new BridgeMessageOrigin("task"), StringContent: "nope")));
        Assert.Null(BridgeMessagingUtilities.ExtractTitleText(new BridgeTitleMessage(
            "user",
            ContentBlocks:
            [
                new BridgeTitleContentBlock("text", "<ide_opened_file>x</ide_opened_file>")
            ])));

        var extracted = BridgeMessagingUtilities.ExtractTitleText(new BridgeTitleMessage(
            "user",
            ContentBlocks:
            [
                new BridgeTitleContentBlock("image"),
                new BridgeTitleContentBlock("text", "<ide_opened_file>x</ide_opened_file>\nActual title")
            ]));

        Assert.Equal("Actual title", extracted);
    }

    [Fact]
    public void MakeResultMessage_Builds_Minimal_Success_Result_Message()
    {
        var message = BridgeMessagingUtilities.MakeResultMessage("session-123");

        Assert.Equal("result", message.Type);
        Assert.Equal("success", message.Subtype);
        Assert.False(message.IsError);
        Assert.Equal(0, message.DurationMs);
        Assert.Equal(0, message.DurationApiMs);
        Assert.Equal(0, message.NumTurns);
        Assert.Equal(string.Empty, message.Result);
        Assert.Null(message.StopReason);
        Assert.Equal(0, message.TotalCostUsd);
        Assert.Equal("session-123", message.SessionId);
        Assert.NotEmpty(message.Uuid);
        Assert.Empty(message.ModelUsage);
        Assert.Empty(message.PermissionDenials);
    }
}
