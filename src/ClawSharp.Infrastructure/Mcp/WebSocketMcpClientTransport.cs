using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ClawSharp.Infrastructure;

public sealed class WebSocketMcpClientTransport : IClientTransport
{
    private readonly Uri _uri;
    private readonly IReadOnlyDictionary<string, string>? _headers;
    private readonly IReadOnlyList<string> _subprotocols;

    public WebSocketMcpClientTransport(
        Uri uri,
        IReadOnlyDictionary<string, string>? headers = null,
        IReadOnlyList<string>? subprotocols = null)
    {
        _uri = uri;
        _headers = headers;
        _subprotocols = subprotocols ?? ["mcp"];
    }

    public string Name => _uri.ToString();

    public async Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
    {
        var socket = new ClientWebSocket();
        foreach (var protocol in _subprotocols)
        {
            socket.Options.AddSubProtocol(protocol);
        }

        if (_headers is not null)
        {
            foreach (var (key, value) in _headers)
            {
                socket.Options.SetRequestHeader(key, value);
            }
        }

        await socket.ConnectAsync(_uri, cancellationToken).ConfigureAwait(false);
        return new WebSocketMcpTransportSession(Name, socket);
    }

    private sealed class WebSocketMcpTransportSession : ITransport
    {
        private readonly ClientWebSocket _socket;
        private readonly Channel<JsonRpcMessage> _messages;
        private readonly CancellationTokenSource _shutdown;
        private readonly Task _receiveLoop;

        public WebSocketMcpTransportSession(string name, ClientWebSocket socket)
        {
            Name = name;
            _socket = socket;
            _messages = Channel.CreateUnbounded<JsonRpcMessage>();
            _shutdown = new CancellationTokenSource();
            _receiveLoop = Task.Run(() => ReceiveLoopAsync(_shutdown.Token));
        }

        public string Name { get; }

        public string? SessionId => null;

        public ChannelReader<JsonRpcMessage> MessageReader => _messages.Reader;

        public async Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
        {
            if (_socket.State != WebSocketState.Open)
            {
                throw new InvalidOperationException("WebSocket transport is not open.");
            }

            var json = JsonSerializer.Serialize(message, McpJsonUtilities.DefaultOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            await _socket.SendAsync(
                    bytes,
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            _shutdown.Cancel();

            try
            {
                if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            catch
            {
                // Ignore transport shutdown errors.
            }

            try
            {
                await _receiveLoop.ConfigureAwait(false);
            }
            catch
            {
                // The receive loop already completed the channel with the error.
            }

            _socket.Dispose();
            _shutdown.Dispose();
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            var assembled = new ArrayBufferWriter<byte>();

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var result = await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    assembled.Write(buffer.AsSpan(0, result.Count));
                    if (!result.EndOfMessage)
                    {
                        continue;
                    }

                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        throw new InvalidOperationException("Unsupported non-text MCP WebSocket message.");
                    }

                    var json = Encoding.UTF8.GetString(assembled.WrittenSpan);
                    assembled.Clear();
                    var message = JsonSerializer.Deserialize<JsonRpcMessage>(json, McpJsonUtilities.DefaultOptions);
                    if (message is not null)
                    {
                        await _messages.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
                    }
                }

                _messages.Writer.TryComplete();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _messages.Writer.TryComplete();
            }
            catch (Exception exception)
            {
                _messages.Writer.TryComplete(exception);
            }
        }
    }
}
