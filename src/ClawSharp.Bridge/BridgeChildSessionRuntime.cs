// TS origin: ./bridge/sessionRunner.ts, ./entrypoints/cli.tsx
using System.Buffers;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace ClawSharp.Bridge;

public interface IBridgeChildSessionConnection : IAsyncDisposable
{
    WebSocketCloseStatus? CloseStatus { get; }

    string? CloseDescription { get; }

    IAsyncEnumerable<JsonObject> ReadAsync(CancellationToken cancellationToken = default);

    Task SendAsync(JsonObject message, CancellationToken cancellationToken = default);

    Task CloseAsync(CancellationToken cancellationToken = default);
}

public sealed record BridgeChildSessionRuntimeDependencies(
    Func<BridgeChildSessionOptions, Action<string>?, CancellationToken, Task<IBridgeChildSessionConnection>> ConnectAsync);

public static class BridgeChildSessionRuntime
{
    public static Task<int> RunAsync(
        BridgeChildSessionOptions options,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(
            options,
            stdin,
            stdout,
            stderr,
            new BridgeChildSessionRuntimeDependencies(CreateDefaultConnectionAsync),
            cancellationToken);
    }

    public static async Task<int> RunAsync(
        BridgeChildSessionOptions options,
        TextReader stdin,
        TextWriter stdout,
        TextWriter stderr,
        BridgeChildSessionRuntimeDependencies dependencies,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(stdin);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
        ArgumentNullException.ThrowIfNull(dependencies);

        using var debugSink = CreateDebugSink(options, stderr);
        var onDebug = debugSink.Log;

        try
        {
            await using var connection = await dependencies.ConnectAsync(options, onDebug, cancellationToken);
            var structuredIo = new BridgeStructuredIo(
                stdin,
                stdout,
                options.ReplayUserMessages,
                new BridgeStructuredIoDependencies(OnDebug: onDebug));
            structuredIo.SetOnControlRequestSent(structuredIo.RegisterPendingRequest);
            structuredIo.SetOnControlRequestResolved(
                requestId => onDebug?.Invoke(
                    $"[bridge:child] sessionId={options.SessionId} resolved control_request request_id={requestId}"));

            using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var runToken = runCancellation.Token;
            var shutdownRequested = false;

            var ingressTask = PumpIngressAsync(
                options,
                connection,
                structuredIo,
                onDebug,
                () => shutdownRequested,
                runToken);
            var egressTask = PumpEgressAsync(
                connection,
                structuredIo,
                onDebug,
                runToken);

            var completed = await Task.WhenAny(ingressTask, egressTask);
            if (completed == ingressTask)
            {
                var ingressExitCode = await ingressTask;
                runCancellation.Cancel();
                await AwaitIgnoreCancellationAsync(egressTask);
                return ingressExitCode;
            }

            shutdownRequested = true;
            onDebug("[bridge:child] stdin closed, closing transport");
            await connection.CloseAsync(cancellationToken);
            await egressTask;
            return await ingressTask;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 1;
        }
        catch (Exception error)
        {
            await stderr.WriteLineAsync($"Error: {error.Message}");
            return 1;
        }
    }

    private static async Task<int> PumpIngressAsync(
        BridgeChildSessionOptions options,
        IBridgeChildSessionConnection connection,
        BridgeStructuredIo structuredIo,
        Action<string>? onDebug,
        Func<bool> shutdownRequested,
        CancellationToken cancellationToken)
    {
        var sawResult = false;
        var sawErrorResult = false;

        await foreach (var message in connection.ReadAsync(cancellationToken))
        {
            await structuredIo.WriteAsync(message, cancellationToken);

            if (!string.Equals(message["type"]?.GetValue<string>(), "result", StringComparison.Ordinal))
            {
                continue;
            }

            sawResult = true;
            var subtype = message["subtype"]?.GetValue<string>();
            sawErrorResult = !string.Equals(subtype, "success", StringComparison.Ordinal);
            onDebug?.Invoke(
                $"[bridge:child] sessionId={options.SessionId} received result subtype={subtype ?? "unknown"}");
            return sawErrorResult ? 1 : 0;
        }

        if (shutdownRequested())
        {
            return 0;
        }

        if (sawResult)
        {
            return sawErrorResult ? 1 : 0;
        }

        var closeSuffix = connection.CloseStatus is null
            ? string.Empty
            : $" ({connection.CloseStatus}{(string.IsNullOrWhiteSpace(connection.CloseDescription) ? string.Empty : $": {connection.CloseDescription}")})";
        throw new InvalidOperationException(
            $"Session transport closed before a result message was received{closeSuffix}.");
    }

    private static async Task PumpEgressAsync(
        IBridgeChildSessionConnection connection,
        BridgeStructuredIo structuredIo,
        Action<string>? onDebug,
        CancellationToken cancellationToken)
    {
        await foreach (var message in structuredIo.ReadAsync(cancellationToken))
        {
            onDebug?.Invoke(
                $"[bridge:child] forwarding stdin message type={message["type"]?.GetValue<string>() ?? "unknown"}");
            await connection.SendAsync(message, cancellationToken);
        }
    }

    private static async Task AwaitIgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task<IBridgeChildSessionConnection> CreateDefaultConnectionAsync(
        BridgeChildSessionOptions options,
        Action<string>? onDebug,
        CancellationToken cancellationToken)
    {
        var accessToken = Environment.GetEnvironmentVariable("CLAUDE_CODE_SESSION_ACCESS_TOKEN");
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidOperationException(
                "CLAUDE_CODE_SESSION_ACCESS_TOKEN is required for hidden bridge child-session mode.");
        }

        if (!Uri.TryCreate(options.SdkUrl, UriKind.Absolute, out var sdkUri))
        {
            throw new InvalidOperationException($"Invalid --sdk-url '{options.SdkUrl}'.");
        }

        return sdkUri.Scheme switch
        {
            "ws" or "wss" => await WebSocketBridgeChildSessionConnection.ConnectAsync(
                sdkUri,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Authorization"] = $"Bearer {accessToken}"
                },
                onDebug,
                cancellationToken),
            "http" or "https" => await ConnectCcrV2Async(options, sdkUri, accessToken, onDebug, cancellationToken),
            _ => throw new InvalidOperationException(
                $"Unsupported sdk-url scheme '{sdkUri.Scheme}'. Expected ws, wss, http, or https.")
        };
    }

    private static async Task<IBridgeChildSessionConnection> ConnectCcrV2Async(
        BridgeChildSessionOptions options,
        Uri sdkUri,
        string accessToken,
        Action<string>? onDebug,
        CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        var apiBaseUrl = sdkUri.GetLeftPart(UriPartial.Authority);
        var trustedDeviceToken = Environment.GetEnvironmentVariable("CLAUDE_CODE_TRUSTED_DEVICE_TOKEN");
        var credentials = await CodeSessionApiClient.FetchRemoteCredentialsAsync(
            new CodeSessionApiDependencies(httpClient, onDebug),
            options.SessionId,
            apiBaseUrl,
            accessToken,
            timeoutMs: 30_000,
            trustedDeviceToken,
            cancellationToken);
        if (credentials is null)
        {
            throw new InvalidOperationException(
                $"Failed to fetch CCR v2 remote credentials for session {options.SessionId}.");
        }

        var expectedWorkerEpoch = Environment.GetEnvironmentVariable("CLAUDE_CODE_WORKER_EPOCH");
        if (!string.IsNullOrWhiteSpace(expectedWorkerEpoch) &&
            !string.Equals(
                expectedWorkerEpoch,
                credentials.WorkerEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            onDebug?.Invoke(
                $"[bridge:child] worker_epoch mismatch env={expectedWorkerEpoch} remote={credentials.WorkerEpoch}");
        }

        var transportUrl = BridgeWorkSecretUtilities.BuildSdkUrl(credentials.ApiBaseUrl, options.SessionId);
        onDebug?.Invoke(
            $"[bridge:child] resolved CCR v2 session ingress transport {transportUrl}");

        return await WebSocketBridgeChildSessionConnection.ConnectAsync(
            new Uri(transportUrl, UriKind.Absolute),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Authorization"] = $"Bearer {credentials.WorkerJwt}"
            },
            onDebug,
            cancellationToken);
    }

    private static DebugSink CreateDebugSink(BridgeChildSessionOptions options, TextWriter stderr)
    {
        StreamWriter? fileWriter = null;
        object gate = new();

        if (!string.IsNullOrWhiteSpace(options.DebugFile))
        {
            var fullPath = Path.GetFullPath(options.DebugFile);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            fileWriter = new StreamWriter(
                new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                new UTF8Encoding(false))
            {
                AutoFlush = true
            };
        }

        void Log(string message)
        {
            var line = $"[{DateTimeOffset.UtcNow:O}] {message}";
            lock (gate)
            {
                fileWriter?.WriteLine(line);
            }

            if (options.Verbose)
            {
                stderr.WriteLine(line);
            }
        }

        return new DebugSink(Log, fileWriter);
    }

    private sealed record DebugSink(Action<string> Log, IDisposable? FileWriter) : IDisposable
    {
        public void Dispose()
        {
            FileWriter?.Dispose();
        }
    }

    private sealed class WebSocketBridgeChildSessionConnection : IBridgeChildSessionConnection
    {
        private readonly ClientWebSocket _socket;
        private readonly Channel<JsonObject> _messages;
        private readonly CancellationTokenSource _receiveShutdown = new();
        private readonly Task _receiveLoop;

        private WebSocketBridgeChildSessionConnection(
            ClientWebSocket socket,
            Action<string>? onDebug)
        {
            _socket = socket;
            _messages = Channel.CreateUnbounded<JsonObject>();
            _receiveLoop = Task.Run(() => ReceiveLoopAsync(onDebug, _receiveShutdown.Token));
        }

        public WebSocketCloseStatus? CloseStatus { get; private set; }

        public string? CloseDescription { get; private set; }

        public static async Task<IBridgeChildSessionConnection> ConnectAsync(
            Uri uri,
            IReadOnlyDictionary<string, string> headers,
            Action<string>? onDebug,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(uri);
            ArgumentNullException.ThrowIfNull(headers);

            var socket = new ClientWebSocket();
            foreach (var pair in headers)
            {
                socket.Options.SetRequestHeader(pair.Key, pair.Value);
            }

            await socket.ConnectAsync(uri, cancellationToken);
            onDebug?.Invoke($"[bridge:child] connected transport {uri}");
            return new WebSocketBridgeChildSessionConnection(socket, onDebug);
        }

        public IAsyncEnumerable<JsonObject> ReadAsync(CancellationToken cancellationToken = default)
        {
            return _messages.Reader.ReadAllAsync(cancellationToken);
        }

        public async Task SendAsync(JsonObject message, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);

            if (_socket.State != WebSocketState.Open)
            {
                throw new InvalidOperationException("Session transport is not open.");
            }

            var payload = Encoding.UTF8.GetBytes(message.ToJsonString());
            await _socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }

        public async Task CloseAsync(CancellationToken cancellationToken = default)
        {
            _receiveShutdown.Cancel();

            try
            {
                if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    await _socket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Closing",
                        cancellationToken);
                }
            }
            catch
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            await CloseAsync();

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

        private async Task ReceiveLoopAsync(Action<string>? onDebug, CancellationToken cancellationToken)
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
                        CloseStatus = _socket.CloseStatus;
                        CloseDescription = _socket.CloseStatusDescription;
                        break;
                    }

                    assembled.Write(buffer.AsSpan(0, result.Count));
                    if (!result.EndOfMessage)
                    {
                        continue;
                    }

                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        throw new InvalidOperationException("Unsupported non-text bridge child transport message.");
                    }

                    var payload = Encoding.UTF8.GetString(assembled.WrittenSpan);
                    assembled.Clear();
                    var parsed = BridgeMessagingUtilities.NormalizeControlMessageKeys(JsonNode.Parse(payload));
                    if (parsed is not JsonObject message)
                    {
                        continue;
                    }

                    await _messages.Writer.WriteAsync(message, cancellationToken);
                }

                _messages.Writer.TryComplete();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _messages.Writer.TryComplete();
            }
            catch (Exception error)
            {
                onDebug?.Invoke($"[bridge:child] transport receive failed: {error.Message}");
                _messages.Writer.TryComplete(error);
            }
        }
    }
}
