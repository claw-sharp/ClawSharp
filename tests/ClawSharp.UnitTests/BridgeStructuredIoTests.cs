using System.Text;
using System.Text.Json.Nodes;
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgeStructuredIoTests
{
    [Fact]
    public async Task ReadAsync_Ignores_KeepAlive_And_Applies_Environment_Updates()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        var io = new BridgeStructuredIo(
            new StringReader(string.Join(
                Environment.NewLine,
                """{"type":"keep_alive"}""",
                """{"type":"update_environment_variables","variables":{"CLAUDE_CODE_SESSION_ACCESS_TOKEN":"token-123","FOO":"bar"}}""",
                """{"type":"user","session_id":"","message":{"role":"user","content":"hello"},"parent_tool_use_id":null}"""
            )),
            new StringWriter(),
            replayUserMessages: false,
            new BridgeStructuredIoDependencies(
                SetEnvironmentVariable: (key, value) => variables[key] = value));

        var messages = await CollectAsync(io.ReadAsync());

        Assert.Single(messages);
        Assert.Equal("user", messages[0]["type"]?.GetValue<string>());
        Assert.Equal("hello", messages[0]["message"]?["content"]?.GetValue<string>());
        Assert.Equal("token-123", variables["CLAUDE_CODE_SESSION_ACCESS_TOKEN"]);
        Assert.Equal("bar", variables["FOO"]);
    }

    [Fact]
    public async Task ReadAsync_Normalizes_Control_Response_And_Suppresses_It_When_Replay_Disabled()
    {
        var io = new BridgeStructuredIo(
            new StringReader("""{"type":"control_response","response":{"subtype":"success","requestId":"req-1","response":{"toolUseID":"tool-1"}}}"""),
            new StringWriter(),
            replayUserMessages: false);
        io.RegisterPendingRequest(
            new JsonObject
            {
                ["type"] = "control_request",
                ["request_id"] = "req-1",
                ["request"] = new JsonObject
                {
                    ["subtype"] = "can_use_tool",
                    ["tool_use_id"] = "tool-1"
                }
            });

        string? resolvedRequestId = null;
        io.SetOnControlRequestResolved(requestId => resolvedRequestId = requestId);

        var messages = await CollectAsync(io.ReadAsync());

        Assert.Empty(messages);
        Assert.Equal("req-1", resolvedRequestId);
        Assert.Empty(io.GetPendingPermissionRequests());
    }

    [Fact]
    public async Task ReadAsync_Replays_Control_Response_When_Enabled()
    {
        var io = new BridgeStructuredIo(
            new StringReader("""{"type":"control_response","response":{"subtype":"success","request_id":"req-1","response":{"toolUseID":"tool-1"}}}"""),
            new StringWriter(),
            replayUserMessages: true);
        io.RegisterPendingRequest(
            new JsonObject
            {
                ["type"] = "control_request",
                ["request_id"] = "req-1",
                ["request"] = new JsonObject
                {
                    ["subtype"] = "can_use_tool",
                    ["tool_use_id"] = "tool-1"
                }
            });

        var messages = await CollectAsync(io.ReadAsync());

        Assert.Single(messages);
        Assert.Equal("control_response", messages[0]["type"]?.GetValue<string>());
        Assert.Equal("req-1", messages[0]["response"]?["request_id"]?.GetValue<string>());
    }

    [Fact]
    public async Task InjectControlResponseAsync_Writes_ControlCancelRequest_And_Removes_Pending_Request()
    {
        var output = new StringWriter(new StringBuilder());
        var io = new BridgeStructuredIo(new StringReader(string.Empty), output, replayUserMessages: false);
        io.RegisterPendingRequest(
            new JsonObject
            {
                ["type"] = "control_request",
                ["request_id"] = "req-1",
                ["request"] = new JsonObject
                {
                    ["subtype"] = "can_use_tool",
                    ["tool_use_id"] = "tool-1"
                }
            });

        var injected = await io.InjectControlResponseAsync(
            new JsonObject
            {
                ["type"] = "control_response",
                ["response"] = new JsonObject
                {
                    ["subtype"] = "success",
                    ["request_id"] = "req-1"
                }
            });

        Assert.True(injected);
        Assert.Empty(io.GetPendingPermissionRequests());

        var payload = JsonNode.Parse(output.ToString())!.AsObject();
        Assert.Equal("control_cancel_request", payload["type"]?.GetValue<string>());
        Assert.Equal("req-1", payload["request_id"]?.GetValue<string>());
    }

    [Fact]
    public async Task ReadAsync_Passes_Through_ControlCancelRequest_And_Result()
    {
        var io = new BridgeStructuredIo(
            new StringReader(string.Join(
                Environment.NewLine,
                """{"type":"control_cancel_request","request_id":"req-1"}""",
                """{"type":"result","subtype":"success","session_id":"session_123","uuid":"uuid_1"}"""
            )),
            new StringWriter(),
            replayUserMessages: false);

        var messages = await CollectAsync(io.ReadAsync());

        Assert.Equal(2, messages.Count);
        Assert.Equal("control_cancel_request", messages[0]["type"]?.GetValue<string>());
        Assert.Equal("req-1", messages[0]["request_id"]?.GetValue<string>());
        Assert.Equal("result", messages[1]["type"]?.GetValue<string>());
        Assert.Equal("success", messages[1]["subtype"]?.GetValue<string>());
    }

    [Fact]
    public async Task PrependUserMessage_Yields_Before_Input_Stream()
    {
        var io = new BridgeStructuredIo(
            new StringReader("""{"type":"assistant","message":{"content":[{"type":"text","text":"later"}]}}"""),
            new StringWriter(),
            replayUserMessages: false);
        io.PrependUserMessage("first");

        var messages = await CollectAsync(io.ReadAsync());

        Assert.Equal(2, messages.Count);
        Assert.Equal("user", messages[0]["type"]?.GetValue<string>());
        Assert.Equal("first", messages[0]["message"]?["content"]?.GetValue<string>());
        Assert.Equal("assistant", messages[1]["type"]?.GetValue<string>());
    }

    private static async Task<List<JsonObject>> CollectAsync(IAsyncEnumerable<JsonObject> source)
    {
        List<JsonObject> messages = [];
        await foreach (var message in source)
        {
            messages.Add(message);
        }

        return messages;
    }
}
