using ClawSharp.Core;
using System.Text.Json.Nodes;

namespace ClawSharp.Bridge;

public sealed record RemoteSessionDirectConnectRequest(
    string BaseUrl,
    string Title,
    IReadOnlyList<RemoteSessionOutboundMessage>? InitialMessages = null,
    IReadOnlyList<string>? Tags = null,
    bool Perpetual = false);

public sealed record RemoteSessionDirectConnectDependencies(
    RemoteSessionBootstrapDependencies BootstrapDependencies,
    Func<string, RemoteCredentials, Action<string>?, CancellationToken, Task<IReconnectableRemoteSessionTransport>> ConnectTransportAsync,
    Action<string, string?>? OnStateChange = null,
    Func<JsonObject, Task>? OnInboundMessage = null,
    Action<JsonObject>? OnPermissionResponse = null,
    Action? OnInterrupt = null,
    Action<string?>? OnSetModel = null,
    Action<int?>? OnSetMaxThinkingTokens = null,
    Func<string, BridgePermissionModeVerdict>? OnSetPermissionMode = null,
    Func<string, string, bool>? OnUserMessage = null,
    Action<string>? OnDebug = null,
    Func<double, Task>? SleepAsync = null,
    Func<double>? NextRandomDouble = null);

public sealed class RemoteSessionDirectConnectRuntime : IAsyncDisposable
{
    private readonly RemoteSessionDirectConnectDependencies _dependencies;
    private readonly RemoteSessionBootstrapCoordinator _bootstrapCoordinator;
    private readonly SemaphoreSlim _rebuildGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly DelegatingRemoteSessionTransport _transport = new();
    private CancellationTokenSource? _refreshCancellation;
    private RemoteSessionManager? _manager;
    private IReconnectableRemoteSessionTransport? _currentTransport;
    private EnvLessBridgeConfig? _config;
    private string? _sessionId;
    private string? _baseUrl;
    private IReadOnlyList<RemoteSessionOutboundMessage>? _initialMessages;
    private bool _started;
    private bool _disposed;
    private bool _refreshInFlight;

    public RemoteSessionDirectConnectRuntime(RemoteSessionDirectConnectDependencies dependencies)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        _bootstrapCoordinator = new RemoteSessionBootstrapCoordinator(
            dependencies.BootstrapDependencies with
            {
                OnStateChange = dependencies.OnStateChange ?? dependencies.BootstrapDependencies.OnStateChange,
                OnDebug = dependencies.OnDebug ?? dependencies.BootstrapDependencies.OnDebug,
                SleepAsync = dependencies.SleepAsync ?? dependencies.BootstrapDependencies.SleepAsync,
                NextRandomDouble = dependencies.NextRandomDouble ?? dependencies.BootstrapDependencies.NextRandomDouble
            });
    }

    public string? BridgeSessionId
    {
        get
        {
            lock (_stateGate)
            {
                return _sessionId;
            }
        }
    }

    public RemoteSessionManager? CurrentManager
    {
        get
        {
            lock (_stateGate)
            {
                return _manager;
            }
        }
    }

    public async Task<RemoteSessionInfo?> StartAsync(
        RemoteSessionDirectConnectRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(RemoteSessionDirectConnectRuntime));
        }

        if (_started)
        {
            return _sessionId is null || _baseUrl is null
                ? null
                : new RemoteSessionInfo(
                    BridgeWorkSecretUtilities.BuildCcrV2SdkUrl(_baseUrl, _sessionId),
                    "connected");
        }

        var bootstrap = await _bootstrapCoordinator.TryBootstrapDirectConnectAsync(
            new RemoteSessionBootstrapRequest(
                request.BaseUrl,
                request.Title,
                request.Perpetual,
                request.Tags),
            cancellationToken);
        if (bootstrap is null)
        {
            return null;
        }

        _initialMessages = request.InitialMessages;
        _baseUrl = request.BaseUrl;
        _sessionId = bootstrap.SessionId;
        _config = bootstrap.Config;

        var manager = new RemoteSessionManager(
            new RemoteSessionManagerDependencies(
                Transport: _transport,
                SessionId: bootstrap.SessionId,
                InitialHistoryCap: 200,
                ArchiveSessionAsync: ArchiveSessionAsync,
                OnStateChange: _dependencies.OnStateChange,
                OnInboundMessage: _dependencies.OnInboundMessage,
                OnPermissionResponse: _dependencies.OnPermissionResponse,
                OnInterrupt: _dependencies.OnInterrupt,
                OnSetModel: _dependencies.OnSetModel,
                OnSetMaxThinkingTokens: _dependencies.OnSetMaxThinkingTokens,
                OnSetPermissionMode: _dependencies.OnSetPermissionMode,
                OnUserMessage: _dependencies.OnUserMessage,
                OnDebug: _dependencies.OnDebug),
            bootstrap.Config.UuidDedupBufferSize,
            request.InitialMessages);

        lock (_stateGate)
        {
            _manager = manager;
            _started = true;
        }

        await BindTransportAsync(
            bootstrap.Credentials,
            replayInitialMessages: true,
            cancellationToken);
        _dependencies.OnStateChange?.Invoke("ready", null);
        ScheduleRefresh(bootstrap.Credentials.ExpiresIn);

        return new RemoteSessionInfo(
            BridgeWorkSecretUtilities.BuildCcrV2SdkUrl(bootstrap.Credentials.ApiBaseUrl, bootstrap.SessionId),
            "connected");
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

        CancelRefresh();
        await manager.TeardownAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelRefresh();

        try
        {
            await TeardownAsync();
        }
        catch
        {
        }

        var transport = _currentTransport;
        _currentTransport = null;
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

        _rebuildGate.Dispose();
        lock (_stateGate)
        {
            _manager = null;
        }
    }

    private async Task BindTransportAsync(
        RemoteCredentials credentials,
        bool replayInitialMessages,
        CancellationToken cancellationToken)
    {
        var sessionId = _sessionId ?? throw new InvalidOperationException("Session bootstrap has not completed.");
        var transport = await _dependencies.ConnectTransportAsync(
            sessionId,
            credentials,
            _dependencies.OnDebug,
            cancellationToken);

        transport.DataReceived += data =>
        {
            if (_disposed || !ReferenceEquals(_currentTransport, transport))
            {
                return;
            }

            CurrentManager?.HandleIngressData(data);
        };

        transport.PermanentlyClosed += code => _ = HandleTransportClosedAsync(transport, code);

        var previousTransport = _currentTransport;
        _currentTransport = transport;
        _transport.Bind(transport);

        if (previousTransport is not null && !ReferenceEquals(previousTransport, transport))
        {
            try
            {
                ((IBridgeReplTransport)previousTransport).Close();
            }
            catch
            {
            }

            try
            {
                await previousTransport.DisposeAsync();
            }
            catch
            {
            }
        }

        await (CurrentManager?.OnTransportConnectedAsync(
                  replayInitialMessages ? _initialMessages : null,
                  cancellationToken) ?? Task.CompletedTask);
    }

    private async Task HandleTransportClosedAsync(
        IReconnectableRemoteSessionTransport transport,
        int? closeCode)
    {
        if (_disposed || !ReferenceEquals(_currentTransport, transport))
        {
            return;
        }

        CurrentManager?.OnTransportClosed(closeCode);
        if (closeCode != 401)
        {
            return;
        }

        await RecoverTransportAsync("JWT expired - refreshing", CancellationToken.None);
    }

    private async Task RecoverTransportAsync(string reconnectDetail, CancellationToken cancellationToken)
    {
        if (_disposed || _config is null || _sessionId is null || _baseUrl is null)
        {
            return;
        }

        await _rebuildGate.WaitAsync(cancellationToken);
        try
        {
            if (_disposed || _config is null || _sessionId is null || _baseUrl is null || _refreshInFlight)
            {
                return;
            }

            _refreshInFlight = true;
            CurrentManager?.SetAuthRecoveryInFlight(true);
            _dependencies.OnStateChange?.Invoke("reconnecting", reconnectDetail);

            var fresh = await WithRetryAsync(
                () => _dependencies.BootstrapDependencies.FetchRemoteCredentialsAsync(
                    _sessionId,
                    _baseUrl,
                    _dependencies.BootstrapDependencies.GetAccessToken() ?? string.Empty,
                    _config.HttpTimeoutMs,
                    _dependencies.BootstrapDependencies.GetTrustedDeviceToken?.Invoke(),
                    cancellationToken),
                "fetchRemoteCredentials",
                _config,
                cancellationToken);
            if (fresh is null)
            {
                _dependencies.OnStateChange?.Invoke("failed", "JWT refresh failed after transport close");
                return;
            }

            await BindTransportAsync(fresh, replayInitialMessages: false, cancellationToken);
            ScheduleRefresh(fresh.ExpiresIn);
        }
        finally
        {
            CurrentManager?.SetAuthRecoveryInFlight(false);
            _refreshInFlight = false;
            _rebuildGate.Release();
        }
    }

    private void ScheduleRefresh(double expiresInSeconds)
    {
        if (_config is null)
        {
            return;
        }

        CancelRefresh();
        var delayMs = Math.Max(
            0d,
            (expiresInSeconds * 1000d) - _config.TokenRefreshBufferMs);
        var refreshCancellation = new CancellationTokenSource();
        _refreshCancellation = refreshCancellation;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(delayMs), refreshCancellation.Token);
                await RecoverTransportAsync("JWT expired - refreshing", refreshCancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }, CancellationToken.None);
    }

    private void CancelRefresh()
    {
        var refreshCancellation = _refreshCancellation;
        _refreshCancellation = null;
        if (refreshCancellation is null)
        {
            return;
        }

        try
        {
            refreshCancellation.Cancel();
        }
        catch
        {
        }
        finally
        {
            refreshCancellation.Dispose();
        }
    }

    private async Task<int?> ArchiveSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (_config is null)
        {
            return null;
        }

        await _dependencies.BootstrapDependencies.ArchiveSessionAsync(
            sessionId,
            _config.TeardownArchiveTimeoutMs,
            cancellationToken);
        return 204;
    }

    private async Task<T?> WithRetryAsync<T>(
        Func<Task<T?>> operation,
        string label,
        EnvLessBridgeConfig config,
        CancellationToken cancellationToken)
        where T : class
    {
        var sleepAsync = _dependencies.SleepAsync ??
                         (delay => Task.Delay(TimeSpan.FromMilliseconds(delay), cancellationToken));
        var nextRandomDouble = _dependencies.NextRandomDouble ?? Random.Shared.NextDouble;

        for (var attempt = 1; attempt <= config.InitRetryMaxAttempts; attempt++)
        {
            var result = await operation();
            if (result is not null)
            {
                return result;
            }

            if (attempt >= config.InitRetryMaxAttempts)
            {
                break;
            }

            var baseDelay = config.InitRetryBaseDelayMs * Math.Pow(2d, attempt - 1);
            var jitter = baseDelay * config.InitRetryJitterFraction * (2d * nextRandomDouble() - 1d);
            var delay = Math.Min(baseDelay + jitter, config.InitRetryMaxDelayMs);
            _dependencies.OnDebug?.Invoke(
                $"[remote-bridge] {label} failed (attempt {attempt}/{config.InitRetryMaxAttempts}), retrying in {Math.Round(delay)}ms");
            await sleepAsync(delay);
        }

        return null;
    }

    private sealed class DelegatingRemoteSessionTransport : IRemoteSessionTransport
    {
        private readonly object _gate = new();
        private IRemoteSessionTransport? _inner;

        public void Bind(IRemoteSessionTransport transport)
        {
            ArgumentNullException.ThrowIfNull(transport);
            lock (_gate)
            {
                _inner = transport;
            }
        }

        public Task WriteAsync(JsonObject message, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);
            return Snapshot()?.WriteAsync(message, cancellationToken) ?? Task.CompletedTask;
        }

        public Task WriteBatchAsync(IReadOnlyList<JsonObject> messages, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(messages);
            return Snapshot()?.WriteBatchAsync(messages, cancellationToken) ?? Task.CompletedTask;
        }

        public void ReportState(string state)
        {
            Snapshot()?.ReportState(state);
        }

        public void Close()
        {
            Snapshot()?.Close();
        }

        private IRemoteSessionTransport? Snapshot()
        {
            lock (_gate)
            {
                return _inner;
            }
        }
    }
}
