// TS origin: ./bridge/replBridgeTransport.ts, ./bridge/sessionRunner.ts
using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace ClawSharp.Bridge;

public sealed class RemoteSessionWebSocketTransport : IReconnectableRemoteSessionTransport
{
    private readonly ClientWebSocket _socket;
    private readonly Channel<string> _messages;
    private readonly CancellationTokenSource _receiveShutdown = new();
    private readonly Task _receiveLoop;
    private readonly Action<string>? _onDebug;
    private int _closeNotified;

    private RemoteSessionWebSocketTransport(ClientWebSocket socket, Action<string>? onDebug)
    {
        _socket = socket;
        _onDebug = onDebug;
        _messages = Channel.CreateUnbounded<string>();
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_receiveShutdown.Token));
        _ = PumpMessagesAsync();
    }

    public event Action<string>? DataReceived;

    public event Action<int?>? PermanentlyClosed;

    public static async Task<IReconnectableRemoteSessionTransport> ConnectAsync(
        string sessionId,
        RemoteCredentials credentials,
        Action<string>? onDebug,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(credentials);

        var url = BridgeWorkSecretUtilities.BuildSdkUrl(credentials.ApiBaseUrl, sessionId);
        var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", $"Bearer {credentials.WorkerJwt}");
        await socket.ConnectAsync(new Uri(url, UriKind.Absolute), cancellationToken);
        onDebug?.Invoke($"[remote-bridge] connected websocket transport {url}");
        return new RemoteSessionWebSocketTransport(socket, onDebug);
    }

    public long GetLastSequenceNum() => 0;

    public Task WriteAsync(JsonObject message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return SendAsync(message.ToJsonString(), cancellationToken);
    }

    public async Task WriteBatchAsync(IReadOnlyList<JsonObject> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        foreach (var message in messages)
        {
            await WriteAsync(message, cancellationToken);
        }
    }

    public void ReportState(string state)
    {
        _onDebug?.Invoke($"[remote-bridge] reportState={state}");
    }

    public void Close()
    {
        _receiveShutdown.Cancel();
        NotifyClosed(_socket.CloseStatus is null ? null : (int)_socket.CloseStatus.Value);

        try
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                _socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Closing",
                    CancellationToken.None).GetAwaiter().GetResult();
            }
        }
        catch
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        Close();

        try
        {
            await _receiveLoop;
        }
        catch
        {
        }

        _socket.Dispose();
        _receiveShutdown.Dispose();
    }

    private async Task SendAsync(string payload, CancellationToken cancellationToken)
    {
        if (_socket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("Remote session transport is not open.");
        }

        var bytes = Encoding.UTF8.GetBytes(payload);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private async Task PumpMessagesAsync()
    {
        try
        {
            await foreach (var payload in _messages.Reader.ReadAllAsync())
            {
                DataReceived?.Invoke(payload);
            }
        }
        catch
        {
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var assembled = new ArrayBufferWriter<byte>();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await _socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    NotifyClosed(_socket.CloseStatus is null ? null : (int)_socket.CloseStatus.Value);
                    break;
                }

                assembled.Write(buffer.AsSpan(0, result.Count));
                if (!result.EndOfMessage)
                {
                    continue;
                }

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    throw new InvalidOperationException("Unsupported non-text remote session transport message.");
                }

                var payload = Encoding.UTF8.GetString(assembled.WrittenSpan);
                assembled.Clear();
                await _messages.Writer.WriteAsync(payload, cancellationToken);
            }

            _messages.Writer.TryComplete();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _messages.Writer.TryComplete();
        }
        catch (Exception error)
        {
            _onDebug?.Invoke($"[remote-bridge] websocket receive failed: {error.Message}");
            NotifyClosed(null);
            _messages.Writer.TryComplete(error);
        }
    }

    private void NotifyClosed(int? closeCode)
    {
        if (Interlocked.Exchange(ref _closeNotified, 1) != 0)
        {
            return;
        }

        PermanentlyClosed?.Invoke(closeCode);
    }
}
