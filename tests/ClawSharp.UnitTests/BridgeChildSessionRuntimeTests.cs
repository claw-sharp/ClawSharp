using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgeChildSessionRuntimeTests
{
    [Fact]
    public async Task RunAsync_Proxies_Stdin_To_Transport_And_Stream_Messages_To_Stdout()
    {
        var connection = new FakeBridgeChildSessionConnection(
        [
            new JsonObject
            {
                ["type"] = "assistant",
                ["message"] = new JsonObject
                {
                    ["content"] = new JsonArray(
                        new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = "hello from bridge"
                        })
                }
            },
            new JsonObject
            {
                ["type"] = "result",
                ["subtype"] = "success",
                ["session_id"] = "session_123",
                ["uuid"] = "uuid_1"
            }
        ]);
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = await BridgeChildSessionRuntime.RunAsync(
            new BridgeChildSessionOptions(
                "wss://example.com/v1/session_ingress/ws/session_123",
                "session_123",
                "stream-json",
                "stream-json",
                ReplayUserMessages: true),
            new ScriptedTextReader(
            [
                """{"type":"user","message":{"role":"user","content":"bridge prompt"}}"""
            ]),
            stdout,
            stderr,
            new BridgeChildSessionRuntimeDependencies((_, _, _) => Task.FromResult<IBridgeChildSessionConnection>(connection)));

        Assert.Equal(0, exitCode);
        Assert.Single(connection.SentMessages);
        Assert.Equal("user", connection.SentMessages[0]["type"]?.GetValue<string>());
        Assert.Contains(@"""hello from bridge""", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains(@"""subtype"":""success""", stdout.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, stderr.ToString());
        Assert.True(connection.CloseCalled);
    }

    [Fact]
    public async Task RunAsync_Returns_Failure_For_Error_Result()
    {
        var connection = new FakeBridgeChildSessionConnection(
        [
            new JsonObject
            {
                ["type"] = "result",
                ["subtype"] = "error",
                ["session_id"] = "session_123",
                ["uuid"] = "uuid_1"
            }
        ]);

        var exitCode = await BridgeChildSessionRuntime.RunAsync(
            new BridgeChildSessionOptions(
                "wss://example.com/v1/session_ingress/ws/session_123",
                "session_123",
                "stream-json",
                "stream-json",
                ReplayUserMessages: true),
            new ScriptedTextReader([]),
            new StringWriter(),
            new StringWriter(),
            new BridgeChildSessionRuntimeDependencies((_, _, _) => Task.FromResult<IBridgeChildSessionConnection>(connection)));

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task RunAsync_Writes_Error_When_Transport_Closes_Before_Result()
    {
        var connection = new FakeBridgeChildSessionConnection([]);
        var stderr = new StringWriter();

        var exitCode = await BridgeChildSessionRuntime.RunAsync(
            new BridgeChildSessionOptions(
                "wss://example.com/v1/session_ingress/ws/session_123",
                "session_123",
                "stream-json",
                "stream-json",
                ReplayUserMessages: true),
            new ScriptedTextReader(
            [
                """{"type":"user","message":{"role":"user","content":"bridge prompt"}}"""
            ]),
            new StringWriter(),
            stderr,
            new BridgeChildSessionRuntimeDependencies((_, _, _) => Task.FromResult<IBridgeChildSessionConnection>(connection)));

        Assert.Equal(1, exitCode);
        Assert.Contains("Session transport closed before a result message was received", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_Forwards_ControlCancelRequest_And_Result_From_Stdin_To_Transport()
    {
        var connection = new FakeBridgeChildSessionConnection(
        [
            new JsonObject
            {
                ["type"] = "result",
                ["subtype"] = "success",
                ["session_id"] = "session_123",
                ["uuid"] = "uuid_remote"
            }
        ]);

        var exitCode = await BridgeChildSessionRuntime.RunAsync(
            new BridgeChildSessionOptions(
                "wss://example.com/v1/session_ingress/ws/session_123",
                "session_123",
                "stream-json",
                "stream-json",
                ReplayUserMessages: true),
            new ScriptedTextReader(
            [
                """{"type":"control_cancel_request","request_id":"req_1"}""",
                """{"type":"result","subtype":"success","session_id":"session_123","uuid":"uuid_local"}"""
            ]),
            new StringWriter(),
            new StringWriter(),
            new BridgeChildSessionRuntimeDependencies((_, _, _) => Task.FromResult<IBridgeChildSessionConnection>(connection)));

        Assert.Equal(0, exitCode);
        Assert.Equal(2, connection.SentMessages.Count);
        Assert.Equal("control_cancel_request", connection.SentMessages[0]["type"]?.GetValue<string>());
        Assert.Equal("req_1", connection.SentMessages[0]["request_id"]?.GetValue<string>());
        Assert.Equal("result", connection.SentMessages[1]["type"]?.GetValue<string>());
        Assert.Equal("success", connection.SentMessages[1]["subtype"]?.GetValue<string>());
    }

    [Fact]
    public async Task RunAsync_Registers_Live_ControlRequests_For_Subsequent_ControlResponses()
    {
        var connection = new FakeBridgeChildSessionConnection(
        [
            new JsonObject
            {
                ["type"] = "control_request",
                ["request_id"] = "req_1",
                ["request"] = new JsonObject
                {
                    ["subtype"] = "can_use_tool",
                    ["tool_use_id"] = "tool_123"
                }
            },
            new JsonObject
            {
                ["type"] = "result",
                ["subtype"] = "success",
                ["session_id"] = "session_123",
                ["uuid"] = "uuid_remote"
            }
        ]);
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = await BridgeChildSessionRuntime.RunAsync(
            new BridgeChildSessionOptions(
                "wss://example.com/v1/session_ingress/ws/session_123",
                "session_123",
                "stream-json",
                "stream-json",
                ReplayUserMessages: true,
                Verbose: true),
            new ScriptedTextReader(
            [
                """{"type":"control_response","response":{"subtype":"success","request_id":"req_1","response":{"toolUseID":"tool_123"}}}"""
            ]),
            stdout,
            stderr,
            new BridgeChildSessionRuntimeDependencies((_, _, _) => Task.FromResult<IBridgeChildSessionConnection>(connection)));

        Assert.Equal(0, exitCode);
        Assert.Contains(@"""type"":""control_request""", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("resolved control_request request_id=req_1", stderr.ToString(), StringComparison.Ordinal);
        Assert.Single(connection.SentMessages);
        Assert.Equal("control_response", connection.SentMessages[0]["type"]?.GetValue<string>());
        Assert.Equal("req_1", connection.SentMessages[0]["response"]?["request_id"]?.GetValue<string>());
    }

    private sealed class FakeBridgeChildSessionConnection(IReadOnlyList<JsonObject> inboundMessages) : IBridgeChildSessionConnection
    {
        public List<JsonObject> SentMessages { get; } = [];

        public bool CloseCalled { get; private set; }

        public WebSocketCloseStatus? CloseStatus => WebSocketCloseStatus.NormalClosure;

        public string? CloseDescription => "done";

        public async IAsyncEnumerable<JsonObject> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var message in inboundMessages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return message.DeepClone()!.AsObject();
                await Task.Yield();
            }
        }

        public Task SendAsync(JsonObject message, CancellationToken cancellationToken = default)
        {
            SentMessages.Add(message.DeepClone()!.AsObject());
            return Task.CompletedTask;
        }

        public Task CloseAsync(CancellationToken cancellationToken = default)
        {
            CloseCalled = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            CloseCalled = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ScriptedTextReader(IReadOnlyList<string> lines) : TextReader
    {
        private readonly Queue<string> _remaining = new(lines);

        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            if (_remaining.Count > 0)
            {
                return ValueTask.FromResult<string?>(_remaining.Dequeue());
            }

            return WaitForCancellationAsync(cancellationToken);
        }

        private static async ValueTask<string?> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }
}
