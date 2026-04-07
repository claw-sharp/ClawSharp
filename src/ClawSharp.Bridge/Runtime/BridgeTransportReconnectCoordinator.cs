namespace ClawSharp.Bridge;

public interface IBridgeReplTransport
{
    long GetLastSequenceNum();

    void Close();
}

public sealed class BridgeTransportReconnectState
{
    public required BridgeConfig BridgeConfig { get; init; }

    public required string EnvironmentId { get; set; }

    public required string EnvironmentSecret { get; set; }

    public required string CurrentSessionId { get; set; }

    public string? CurrentWorkId { get; set; }

    public string? CurrentIngressToken { get; set; }

    public long LastTransportSequenceNum { get; set; }

    public int EnvironmentRecreations { get; set; }

    public int V2Generation { get; set; }

    public bool HadTransportBeforeReconnect { get; set; }

    public IBridgeReplTransport? Transport { get; set; }
}

public sealed record BridgeTransportReconnectDependencies(
    IBridgeApiClient Api,
    BridgeTransportReconnectState State,
    Action WakePollLoop,
    Func<int> DropFlushGate,
    Func<bool> IsPollAborted,
    Func<string?> GetCurrentTitle,
    Func<string, string?, CancellationToken, Task<string?>> CreateSessionAsync,
    Func<string, CancellationToken, Task> ArchiveSessionAsync,
    Func<string, string, CancellationToken, Task> WriteBridgePointerAsync,
    Action ClearRecentInboundUuids,
    Action? ResetUserMessageCallback = null,
    Action? ClearPreviouslyFlushedUuids = null,
    Action<string>? OnDebug = null,
    Action<string, string?>? OnStateChange = null,
    Action? TriggerTeardown = null,
    Action? AbortPollLoop = null,
    Func<string?, Task>? PublishSessionBridgeIdAsync = null,
    int MaxEnvironmentRecreations = 3);

public sealed class BridgeTransportReconnectCoordinator
{
    private readonly BridgeTransportReconnectDependencies _dependencies;
    private readonly object _reconnectGate = new();
    private Task<bool>? _reconnectTask;

    public BridgeTransportReconnectCoordinator(BridgeTransportReconnectDependencies dependencies)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
    }

    public Task<bool> ReconnectEnvironmentWithSessionAsync(CancellationToken cancellationToken = default)
    {
        Task<bool> reconnectTask;
        lock (_reconnectGate)
        {
            reconnectTask = _reconnectTask ??= DoReconnectAsync(cancellationToken);
        }

        return AwaitReconnectAsync(reconnectTask);
    }

    public async Task HandleTransportPermanentCloseAsync(int? closeCode, CancellationToken cancellationToken = default)
    {
        _dependencies.OnDebug?.Invoke(
            $"[bridge:repl] Transport permanently closed: code={closeCode}");

        var state = _dependencies.State;
        if (state.Transport is not null)
        {
            state.HadTransportBeforeReconnect = true;
            var closedSeq = state.Transport.GetLastSequenceNum();
            state.LastTransportSequenceNum = closedSeq;
            state.Transport = null;
        }

        _dependencies.WakePollLoop();

        var dropped = _dependencies.DropFlushGate();
        if (dropped > 0)
        {
            _dependencies.OnDebug?.Invoke(
                $"[bridge:repl] Dropping {dropped} pending message(s) on transport close (code={closeCode})");
        }

        if (closeCode == 1000)
        {
            _dependencies.OnStateChange?.Invoke("failed", "session ended");
            _dependencies.AbortPollLoop?.Invoke();
            _dependencies.TriggerTeardown?.Invoke();
            return;
        }

        _dependencies.OnStateChange?.Invoke(
            "reconnecting",
            $"Remote Control connection lost (code {closeCode})");
        _dependencies.OnDebug?.Invoke(
            $"[bridge:repl] Transport reconnect budget exhausted (code={closeCode}), attempting env reconnect");

        var success = await ReconnectEnvironmentWithSessionAsync(cancellationToken);
        if (success || _dependencies.IsPollAborted())
        {
            return;
        }

        _dependencies.OnDebug?.Invoke(
            "[bridge:repl] reconnectEnvironmentWithSession resolved false — tearing down");
        _dependencies.OnStateChange?.Invoke("failed", "reconnection failed");
        _dependencies.TriggerTeardown?.Invoke();
    }

    public async Task<bool> TryReconnectInPlaceAsync(
        string requestedEnvironmentId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestedEnvironmentId);
        ArgumentNullException.ThrowIfNull(sessionId);

        var state = _dependencies.State;
        if (!string.Equals(state.EnvironmentId, requestedEnvironmentId, StringComparison.Ordinal))
        {
            _dependencies.OnDebug?.Invoke(
                $"[bridge:repl] Env mismatch (requested {requestedEnvironmentId}, got {state.EnvironmentId}) — cannot reconnect in place");
            return false;
        }

        var infraId = BridgeSessionIdCompat.ToInfraSessionId(sessionId);
        var candidates = string.Equals(infraId, sessionId, StringComparison.Ordinal)
            ? new[] { sessionId }
            : new[] { sessionId, infraId };

        foreach (var candidateId in candidates)
        {
            try
            {
                await _dependencies.Api.ReconnectSessionAsync(
                    state.EnvironmentId,
                    candidateId,
                    cancellationToken);
                _dependencies.OnDebug?.Invoke(
                    $"[bridge:repl] Reconnected session {candidateId} in place on env {state.EnvironmentId}");
                return true;
            }
            catch (Exception error)
            {
                _dependencies.OnDebug?.Invoke(
                    $"[bridge:repl] reconnectSession({candidateId}) failed: {error.Message}");
            }
        }

        _dependencies.OnDebug?.Invoke(
            "[bridge:repl] reconnectSession exhausted — falling through to fresh session");
        return false;
    }

    private async Task<bool> AwaitReconnectAsync(Task<bool> reconnectTask)
    {
        try
        {
            return await reconnectTask;
        }
        finally
        {
            lock (_reconnectGate)
            {
                if (ReferenceEquals(_reconnectTask, reconnectTask))
                {
                    _reconnectTask = null;
                }
            }
        }
    }

    private async Task<bool> DoReconnectAsync(CancellationToken cancellationToken)
    {
        var state = _dependencies.State;
        try
        {
            var hadTransport = state.Transport is not null || state.HadTransportBeforeReconnect;
            state.EnvironmentRecreations++;
            state.V2Generation++;
            _dependencies.OnDebug?.Invoke(
                $"[bridge:repl] Reconnecting after env lost (attempt {state.EnvironmentRecreations}/{_dependencies.MaxEnvironmentRecreations})");

            if (state.EnvironmentRecreations > _dependencies.MaxEnvironmentRecreations)
            {
                _dependencies.OnDebug?.Invoke(
                    $"[bridge:repl] Environment reconnect limit reached ({_dependencies.MaxEnvironmentRecreations}), giving up");
                return false;
            }

            if (state.Transport is not null)
            {
                var sequence = state.Transport.GetLastSequenceNum();
                state.LastTransportSequenceNum = sequence;

                state.Transport.Close();
                state.Transport = null;
            }

            _dependencies.WakePollLoop();
            _dependencies.DropFlushGate();

            if (state.CurrentWorkId is not null)
            {
                var workIdBeingCleared = state.CurrentWorkId;
                try
                {
                    await _dependencies.Api.StopWorkAsync(
                        state.EnvironmentId,
                        workIdBeingCleared,
                        false,
                        cancellationToken);
                }
                catch
                {
                }

                if (!string.Equals(state.CurrentWorkId, workIdBeingCleared, StringComparison.Ordinal))
                {
                    _dependencies.OnDebug?.Invoke(
                        "[bridge:repl] Poll loop recovered during stopWork await — deferring to it");
                    state.EnvironmentRecreations = 0;
                    return true;
                }

                state.CurrentWorkId = null;
                state.CurrentIngressToken = null;
            }

            if (_dependencies.IsPollAborted())
            {
                _dependencies.OnDebug?.Invoke("[bridge:repl] Reconnect aborted by teardown");
                return false;
            }

            var requestedEnvironmentId = state.EnvironmentId;
            var reconnectConfig = state.BridgeConfig with { ReuseEnvironmentId = requestedEnvironmentId };
            try
            {
                var registration = await _dependencies.Api.RegisterBridgeEnvironmentAsync(
                    reconnectConfig,
                    cancellationToken);
                state.EnvironmentId = registration.EnvironmentId;
                state.EnvironmentSecret = registration.EnvironmentSecret;
            }
            catch (Exception error)
            {
                _dependencies.OnDebug?.Invoke(
                    $"[bridge:repl] Environment re-registration failed: {error.Message}");
                return false;
            }

            _dependencies.OnDebug?.Invoke(
                $"[bridge:repl] Re-registered: requested={requestedEnvironmentId} got={state.EnvironmentId}");

            if (_dependencies.IsPollAborted())
            {
                _dependencies.OnDebug?.Invoke(
                    "[bridge:repl] Reconnect aborted after env registration, cleaning up");
                try
                {
                    await _dependencies.Api.DeregisterEnvironmentAsync(state.EnvironmentId, cancellationToken);
                }
                catch
                {
                }

                return false;
            }

            if (state.Transport is not null)
            {
                _dependencies.OnDebug?.Invoke(
                    "[bridge:repl] Poll loop recovered during registerBridgeEnvironment await — deferring to it");
                state.EnvironmentRecreations = 0;
                return true;
            }

            if (hadTransport &&
                await TryReconnectInPlaceAsync(requestedEnvironmentId, state.CurrentSessionId, cancellationToken))
            {
                state.EnvironmentRecreations = 0;
                return true;
            }

            await _dependencies.ArchiveSessionAsync(state.CurrentSessionId, cancellationToken);

            if (_dependencies.IsPollAborted())
            {
                _dependencies.OnDebug?.Invoke(
                    "[bridge:repl] Reconnect aborted after archive, cleaning up");
                try
                {
                    await _dependencies.Api.DeregisterEnvironmentAsync(state.EnvironmentId, cancellationToken);
                }
                catch
                {
                }

                return false;
            }

            var currentTitle = _dependencies.GetCurrentTitle();
            var newSessionId = await _dependencies.CreateSessionAsync(
                state.EnvironmentId,
                currentTitle,
                cancellationToken);
            if (string.IsNullOrEmpty(newSessionId))
            {
                _dependencies.OnDebug?.Invoke(
                    "[bridge:repl] Session creation failed during reconnection");
                return false;
            }

            if (_dependencies.IsPollAborted())
            {
                _dependencies.OnDebug?.Invoke(
                    "[bridge:repl] Reconnect aborted after session creation, cleaning up");
                await _dependencies.ArchiveSessionAsync(newSessionId, cancellationToken);
                return false;
            }

            state.CurrentSessionId = newSessionId;
            try
            {
                await (_dependencies.PublishSessionBridgeIdAsync?.Invoke(
                    BridgeSessionIdCompat.ToCompatSessionId(newSessionId)) ?? Task.CompletedTask);
            }
            catch
            {
            }

            state.LastTransportSequenceNum = 0;
            _dependencies.ClearRecentInboundUuids();
            _dependencies.ResetUserMessageCallback?.Invoke();
            _dependencies.OnDebug?.Invoke($"[bridge:repl] Re-created session: {state.CurrentSessionId}");

            await _dependencies.WriteBridgePointerAsync(
                state.CurrentSessionId,
                state.EnvironmentId,
                cancellationToken);
            _dependencies.ClearPreviouslyFlushedUuids?.Invoke();
            state.EnvironmentRecreations = 0;

            return true;
        }
        finally
        {
            state.HadTransportBeforeReconnect = false;
        }
    }
}
