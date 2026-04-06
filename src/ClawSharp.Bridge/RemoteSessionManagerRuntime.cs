// TS origin: ./bridge/remoteBridgeCore.ts, ./bridge/replBridge.ts
using System.Text.Json.Nodes;

namespace ClawSharp.Bridge;

public sealed record RemoteSessionManagerRuntimeDependencies(
    BridgeTransportReconnectDependencies ReconnectDependencies,
    Func<BridgeTransportReconnectState, CancellationToken, Task<IReconnectableRemoteSessionTransport>> ConnectTransportAsync,
    Func<string, CancellationToken, Task<int?>> ArchiveSessionAsync,
    int InitialHistoryCap,
    int UuidDedupBufferSize,
    IReadOnlyList<RemoteSessionOutboundMessage>? InitialMessages = null,
    Func<IRemoteSessionHeartbeatRuntime>? CreateHeartbeatRuntime = null,
    Action<string, string?>? OnStateChange = null,
    Func<JsonObject, Task>? OnInboundMessage = null,
    Action<JsonObject>? OnPermissionResponse = null,
    Action? OnInterrupt = null,
    Action<string?>? OnSetModel = null,
    Action<int?>? OnSetMaxThinkingTokens = null,
    Func<string, BridgePermissionModeVerdict>? OnSetPermissionMode = null,
    Func<string, string, bool>? OnUserMessage = null,
    Action<string>? OnDebug = null);

public sealed class RemoteSessionManagerRuntime : IAsyncDisposable
{
    private readonly RemoteSessionManagerRuntimeDependencies _dependencies;
    private readonly RemoteSessionTransportReconnectRuntime _reconnectRuntime;
    private readonly object _managerGate = new();
    private RemoteSessionManager? _currentManager;
    private bool _disposed;

    public RemoteSessionManagerRuntime(RemoteSessionManagerRuntimeDependencies dependencies)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _reconnectRuntime = new RemoteSessionTransportReconnectRuntime(
            new RemoteSessionTransportReconnectRuntimeDependencies(
                ReconnectDependencies: dependencies.ReconnectDependencies,
                ConnectTransportAsync: dependencies.ConnectTransportAsync,
                CreateSessionClient: CreateSessionClient,
                InitialMessages: dependencies.InitialMessages,
                CreateHeartbeatRuntime: dependencies.CreateHeartbeatRuntime,
                OnDebug: dependencies.OnDebug));
    }

    public RemoteSessionManager? CurrentManager
    {
        get
        {
            lock (_managerGate)
            {
                return _currentManager;
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return _reconnectRuntime.StartAsync(cancellationToken);
    }

    public void WriteMessages(IReadOnlyList<RemoteSessionOutboundMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        CurrentManager?.WriteMessages(messages);
    }

    public void WriteSdkMessages(IReadOnlyList<JsonObject> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        CurrentManager?.WriteSdkMessages(messages);
    }

    public Task SendControlRequestAsync(JsonObject request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CurrentManager?.SendControlRequestAsync(request, cancellationToken) ?? Task.CompletedTask;
    }

    public Task SendControlResponseAsync(JsonObject response, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        return CurrentManager?.SendControlResponseAsync(response, cancellationToken) ?? Task.CompletedTask;
    }

    public Task SendControlCancelRequestAsync(string requestId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestId);
        return CurrentManager?.SendControlCancelRequestAsync(requestId, cancellationToken) ?? Task.CompletedTask;
    }

    public Task SendResultAsync(CancellationToken cancellationToken = default)
    {
        return CurrentManager?.SendResultAsync(cancellationToken) ?? Task.CompletedTask;
    }

    public async Task TeardownAsync(CancellationToken cancellationToken = default)
    {
        var manager = CurrentManager;
        if (manager is null || manager.TornDown)
        {
            return;
        }

        await manager.TeardownAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            await TeardownAsync();
        }
        catch
        {
        }

        await _reconnectRuntime.DisposeAsync();
        lock (_managerGate)
        {
            _currentManager = null;
        }
    }

    private RemoteSessionManager CreateSessionClient(IReconnectableRemoteSessionTransport transport, string sessionId)
    {
        var manager = new RemoteSessionManager(
            new RemoteSessionManagerDependencies(
                Transport: transport,
                SessionId: sessionId,
                InitialHistoryCap: _dependencies.InitialHistoryCap,
                ArchiveSessionAsync: _dependencies.ArchiveSessionAsync,
                OnStateChange: _dependencies.OnStateChange,
                OnInboundMessage: _dependencies.OnInboundMessage,
                OnPermissionResponse: _dependencies.OnPermissionResponse,
                OnInterrupt: _dependencies.OnInterrupt,
                OnSetModel: _dependencies.OnSetModel,
                OnSetMaxThinkingTokens: _dependencies.OnSetMaxThinkingTokens,
                OnSetPermissionMode: _dependencies.OnSetPermissionMode,
                OnUserMessage: _dependencies.OnUserMessage,
                OnDebug: _dependencies.OnDebug),
            _dependencies.UuidDedupBufferSize,
            _dependencies.InitialMessages);

        lock (_managerGate)
        {
            _currentManager = manager;
        }

        return manager;
    }
}
