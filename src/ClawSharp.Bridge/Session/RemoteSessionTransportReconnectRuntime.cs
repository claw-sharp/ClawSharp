namespace ClawSharp.Bridge;

public interface IRemoteBridgeSessionClient : IReplBridgeHandle
{
    Task OnTransportConnectedAsync(
        IReadOnlyList<RemoteSessionOutboundMessage>? initialMessages,
        CancellationToken cancellationToken = default);

    void HandleIngressData(string data);
}

public interface IReconnectableRemoteSessionTransport : IRemoteSessionTransport, IBridgeReplTransport, IAsyncDisposable
{
    event Action<string>? DataReceived;

    event Action<int?>? PermanentlyClosed;
}

public sealed record RemoteSessionTransportReconnectRuntimeDependencies(
    BridgeTransportReconnectDependencies ReconnectDependencies,
    Func<BridgeTransportReconnectState, CancellationToken, Task<IReconnectableRemoteSessionTransport>> ConnectTransportAsync,
    Func<IReconnectableRemoteSessionTransport, string, IRemoteBridgeSessionClient> CreateSessionClient,
    IReadOnlyList<RemoteSessionOutboundMessage>? InitialMessages = null,
    Func<IRemoteSessionHeartbeatRuntime>? CreateHeartbeatRuntime = null,
    Action<string>? OnDebug = null);

public sealed class RemoteSessionTransportReconnectRuntime : IAsyncDisposable
{
    private readonly RemoteSessionTransportReconnectRuntimeDependencies _dependencies;
    private readonly BridgeTransportReconnectCoordinator _reconnectCoordinator;
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly Action? _innerTriggerTeardown;
    private readonly IRemoteSessionHeartbeatRuntime? _heartbeatRuntime;
    private IReconnectableRemoteSessionTransport? _transport;
    private IRemoteBridgeSessionClient? _client;
    private string? _connectedSessionId;
    private bool _teardownTriggered;
    private bool _disposed;

    public RemoteSessionTransportReconnectRuntime(RemoteSessionTransportReconnectRuntimeDependencies dependencies)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _innerTriggerTeardown = dependencies.ReconnectDependencies.TriggerTeardown;
        _heartbeatRuntime = dependencies.CreateHeartbeatRuntime?.Invoke();
        _reconnectCoordinator = new BridgeTransportReconnectCoordinator(
            dependencies.ReconnectDependencies with
            {
                TriggerTeardown = TriggerTeardown
            });
    }

    public IRemoteBridgeSessionClient? CurrentClient => _client;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await ConnectIfNeededAsync(replayInitialMessages: true, cancellationToken);
        _heartbeatRuntime?.Start();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReplBridgeHandleRegistry.SetReplBridgeHandle(null);

        var transport = _transport;
        var heartbeatRuntime = _heartbeatRuntime;
        _transport = null;
        _client = null;
        _connectedSessionId = null;

        if (heartbeatRuntime is not null)
        {
            await heartbeatRuntime.DisposeAsync();
        }

        if (transport is not null)
        {
            try
            {
                ((IBridgeReplTransport)transport).Close();
            }
            catch
            {
            }

            await transport.DisposeAsync();
        }

        _connectGate.Dispose();
    }

    private async Task ConnectIfNeededAsync(bool replayInitialMessages, CancellationToken cancellationToken)
    {
        await _connectGate.WaitAsync(cancellationToken);
        try
        {
            if (_disposed || _teardownTriggered || _transport is not null)
            {
                return;
            }

            var state = _dependencies.ReconnectDependencies.State;
            var transport = await _dependencies.ConnectTransportAsync(state, cancellationToken);
            var sessionId = state.CurrentSessionId;
            var client = _dependencies.CreateSessionClient(transport, sessionId);

            transport.DataReceived += data =>
            {
                if (_disposed || !ReferenceEquals(_transport, transport))
                {
                    return;
                }

                _client?.HandleIngressData(data);
            };
            transport.PermanentlyClosed += code => _ = HandleTransportClosedAsync(transport, code);

            _transport = transport;
            _client = client;
            _connectedSessionId = sessionId;
            state.Transport = transport;
            ReplBridgeHandleRegistry.SetReplBridgeHandle(client);

            _dependencies.OnDebug?.Invoke(
                $"[bridge:repl] Bound transport for session {sessionId}");
            await client.OnTransportConnectedAsync(
                replayInitialMessages ? _dependencies.InitialMessages : null,
                cancellationToken);
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private async Task HandleTransportClosedAsync(IReconnectableRemoteSessionTransport transport, int? closeCode)
    {
        if (_disposed)
        {
            return;
        }

        string? previousSessionId;
        await _connectGate.WaitAsync();
        try
        {
            if (_disposed || !ReferenceEquals(_transport, transport))
            {
                return;
            }

            previousSessionId = _connectedSessionId;
            _transport = null;
            _client = null;
            _connectedSessionId = null;
        }
        finally
        {
            _connectGate.Release();
        }

        try
        {
            await _reconnectCoordinator.HandleTransportPermanentCloseAsync(closeCode, CancellationToken.None);
        }
        finally
        {
            try
            {
                await transport.DisposeAsync();
            }
            catch
            {
            }
        }

        if (_disposed || _teardownTriggered || _dependencies.ReconnectDependencies.IsPollAborted())
        {
            return;
        }

        var currentSessionId = _dependencies.ReconnectDependencies.State.CurrentSessionId;
        var replayInitialMessages = !string.Equals(previousSessionId, currentSessionId, StringComparison.Ordinal);
        await ConnectIfNeededAsync(replayInitialMessages, CancellationToken.None);
    }

    private void TriggerTeardown()
    {
        _teardownTriggered = true;
        _innerTriggerTeardown?.Invoke();
    }
}
