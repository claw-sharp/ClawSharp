using ClawSharp.Core;
using System.Text.Json.Nodes;

namespace ClawSharp.Bridge;

public interface IRemoteSessionTransport : IBridgeControlEventTransport
{
    Task WriteBatchAsync(IReadOnlyList<JsonObject> messages, CancellationToken cancellationToken = default);

    void ReportState(string state);

    void Close();
}

public sealed record RemoteSessionOutboundMessage(
    string Type,
    string Uuid,
    JsonObject SdkEvent,
    bool IsVirtual = false,
    string? Subtype = null,
    BridgeTitleMessage? TitleMessage = null);

public sealed record RemoteSessionManagerDependencies(
    IRemoteSessionTransport Transport,
    string SessionId,
    int InitialHistoryCap,
    Func<string, CancellationToken, Task<int?>> ArchiveSessionAsync,
    Action<string, string?>? OnStateChange = null,
    Func<JsonObject, Task>? OnInboundMessage = null,
    Action<JsonObject>? OnPermissionResponse = null,
    Action? OnInterrupt = null,
    Action<string?>? OnSetModel = null,
    Action<int?>? OnSetMaxThinkingTokens = null,
    Func<string, BridgePermissionModeVerdict>? OnSetPermissionMode = null,
    Func<string, string, bool>? OnUserMessage = null,
    Action<string>? OnDebug = null);

public sealed class RemoteSessionManager : IRemoteBridgeSessionClient
{
    private readonly RemoteSessionManagerDependencies _dependencies;
    private readonly BoundedUuidSet _recentPostedUuids;
    private readonly HashSet<string> _initialMessageUuids = new(StringComparer.Ordinal);
    private readonly BoundedUuidSet _recentInboundUuids;
    private readonly FlushGate<RemoteSessionOutboundMessage> _flushGate = new();
    private bool _initialFlushDone;
    private bool _tornDown;
    private bool _authRecoveryInFlight;
    private bool _userMessageCallbackDone;

    public RemoteSessionManager(
        RemoteSessionManagerDependencies dependencies,
        int uuidDedupBufferSize,
        IReadOnlyList<RemoteSessionOutboundMessage>? initialMessages = null)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        if (uuidDedupBufferSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(uuidDedupBufferSize));
        }

        _recentPostedUuids = new BoundedUuidSet(uuidDedupBufferSize);
        _recentInboundUuids = new BoundedUuidSet(uuidDedupBufferSize);
        _userMessageCallbackDone = dependencies.OnUserMessage is null;

        if (initialMessages is not null)
        {
            foreach (var message in initialMessages)
            {
                _initialMessageUuids.Add(message.Uuid);
                _recentPostedUuids.Add(message.Uuid);
            }

            if (initialMessages.Count > 0)
            {
                _flushGate.Start();
            }
        }
    }

    public string BridgeSessionId => _dependencies.SessionId;

    public bool TornDown => _tornDown;

    public bool AuthRecoveryInFlight => _authRecoveryInFlight;

    public void SetAuthRecoveryInFlight(bool value)
    {
        _authRecoveryInFlight = value;
    }

    public async Task OnTransportConnectedAsync(
        IReadOnlyList<RemoteSessionOutboundMessage>? initialMessages,
        CancellationToken cancellationToken = default)
    {
        _dependencies.OnDebug?.Invoke("[remote-bridge] v2 transport connected");
        ClawSharpTelemetry.LogEvent(
            "tengu_bridge_repl_ws_connected",
            new Dictionary<string, object?>
            {
                ["v2"] = true,
                ["session_id"] = _dependencies.SessionId
            });
        ClawSharpTelemetry.RecordMetric("remote_bridge.connected.count");

        if (!_initialFlushDone && initialMessages is { Count: > 0 })
        {
            _initialFlushDone = true;
            await FlushHistoryAsync(initialMessages, cancellationToken);
            if (!_flushGate.Active)
            {
                _dependencies.OnStateChange?.Invoke("connected", null);
            }
        }
        else if (!_flushGate.Active)
        {
            _dependencies.OnStateChange?.Invoke("connected", null);
        }
    }

    public void HandleIngressData(string data)
    {
        BridgeMessagingUtilities.HandleIngressMessage(
            data,
            _recentPostedUuids,
            _recentInboundUuids,
            _dependencies.OnInboundMessage,
            _dependencies.OnPermissionResponse is null
                ? null
                : response =>
                {
                    _dependencies.Transport.ReportState("running");
                    _dependencies.OnPermissionResponse(response);
                },
            request =>
            {
                _ = BridgeControlEventRouter.HandleServerControlRequestAsync(
                    request,
                    new BridgeServerControlRequestHandlers(
                        _dependencies.Transport,
                        _dependencies.SessionId,
                        OnInterrupt: _dependencies.OnInterrupt,
                        OnSetModel: _dependencies.OnSetModel,
                        OnSetMaxThinkingTokens: _dependencies.OnSetMaxThinkingTokens,
                        OnSetPermissionMode: _dependencies.OnSetPermissionMode,
                        OnDebug: _dependencies.OnDebug));
            },
            _dependencies.OnDebug);
    }

    public void OnTransportClosed(int? code)
    {
        if (_tornDown)
        {
            return;
        }

        _dependencies.OnDebug?.Invoke($"[remote-bridge] v2 transport closed (code={code})");
        ClawSharpTelemetry.LogEvent(
            "tengu_bridge_repl_ws_closed",
            new Dictionary<string, object?>
            {
                ["code"] = code,
                ["v2"] = true,
                ["session_id"] = _dependencies.SessionId
            });
        ClawSharpTelemetry.RecordMetric(
            "remote_bridge.closed.count",
            1,
            new Dictionary<string, object?>
            {
                ["code"] = code
            });
        if (code == 401 && !_authRecoveryInFlight)
        {
            return;
        }

        _dependencies.OnStateChange?.Invoke("failed", $"Transport closed (code {code})");
    }

    public void WriteMessages(IReadOnlyList<RemoteSessionOutboundMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var filtered = messages
            .Where(message =>
                BridgeMessagingUtilities.IsEligibleBridgeMessage(
                    new BridgeMessageEnvelope(message.Type, message.IsVirtual, message.Subtype)) &&
                !_initialMessageUuids.Contains(message.Uuid) &&
                !_recentPostedUuids.Contains(message.Uuid))
            .ToList();
        if (filtered.Count == 0)
        {
            return;
        }

        if (!_userMessageCallbackDone)
        {
            foreach (var message in filtered)
            {
                var titleText = message.TitleMessage is null
                    ? null
                    : BridgeMessagingUtilities.ExtractTitleText(message.TitleMessage);
                if (titleText is not null &&
                    (_dependencies.OnUserMessage?.Invoke(titleText, _dependencies.SessionId) ?? false))
                {
                    _userMessageCallbackDone = true;
                    break;
                }
            }
        }

        if (_flushGate.Enqueue(filtered.ToArray()))
        {
            _dependencies.OnDebug?.Invoke(
                $"[remote-bridge] Queued {filtered.Count} message(s) during flush");
            return;
        }

        foreach (var message in filtered)
        {
            _recentPostedUuids.Add(message.Uuid);
        }

        if (filtered.Any(message => string.Equals(message.Type, "user", StringComparison.Ordinal)))
        {
            _dependencies.Transport.ReportState("running");
        }

        _dependencies.OnDebug?.Invoke($"[remote-bridge] Sending {filtered.Count} message(s)");
        _ = _dependencies.Transport.WriteBatchAsync(
            filtered.Select(message => AttachSessionId(message.SdkEvent)).ToArray());
    }

    public void WriteSdkMessages(IReadOnlyList<JsonObject> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var filtered = messages
            .Where(message =>
            {
                var uuid = message["uuid"]?.GetValue<string>();
                return uuid is null || !_recentPostedUuids.Contains(uuid);
            })
            .Select(message => AttachSessionId(message))
            .ToList();

        if (filtered.Count == 0)
        {
            return;
        }

        foreach (var message in filtered)
        {
            var uuid = message["uuid"]?.GetValue<string>();
            if (uuid is not null)
            {
                _recentPostedUuids.Add(uuid);
            }
        }

        _ = _dependencies.Transport.WriteBatchAsync(filtered);
    }

    public Task SendControlRequestAsync(JsonObject request, CancellationToken cancellationToken = default)
    {
        return BridgeControlEventRouter.SendControlRequestAsync(
            request,
            new BridgeOutboundControlEventHandlers(
                _dependencies.Transport,
                _dependencies.SessionId,
                AuthRecoveryInFlight: _authRecoveryInFlight,
                IsRemoteBridge: true,
                ReportState: _dependencies.Transport.ReportState,
                OnDebug: _dependencies.OnDebug),
            cancellationToken);
    }

    public Task SendControlResponseAsync(JsonObject response, CancellationToken cancellationToken = default)
    {
        return BridgeControlEventRouter.SendControlResponseAsync(
            response,
            new BridgeOutboundControlEventHandlers(
                _dependencies.Transport,
                _dependencies.SessionId,
                AuthRecoveryInFlight: _authRecoveryInFlight,
                IsRemoteBridge: true,
                ReportState: _dependencies.Transport.ReportState,
                OnDebug: _dependencies.OnDebug),
            cancellationToken);
    }

    public Task SendControlCancelRequestAsync(string requestId, CancellationToken cancellationToken = default)
    {
        return BridgeControlEventRouter.SendControlCancelRequestAsync(
            requestId,
            new BridgeOutboundControlEventHandlers(
                _dependencies.Transport,
                _dependencies.SessionId,
                AuthRecoveryInFlight: _authRecoveryInFlight,
                IsRemoteBridge: true,
                ReportState: _dependencies.Transport.ReportState,
                OnDebug: _dependencies.OnDebug),
            cancellationToken);
    }

    public Task SendResultAsync(CancellationToken cancellationToken = default)
    {
        return BridgeControlEventRouter.SendResultAsync(
            new BridgeOutboundControlEventHandlers(
                _dependencies.Transport,
                _dependencies.SessionId,
                AuthRecoveryInFlight: _authRecoveryInFlight,
                IsRemoteBridge: true,
                ReportState: _dependencies.Transport.ReportState,
                OnDebug: _dependencies.OnDebug),
            cancellationToken);
    }

    public async Task TeardownAsync(CancellationToken cancellationToken = default)
    {
        if (_tornDown)
        {
            return;
        }

        _tornDown = true;
        _flushGate.Drop();

        _dependencies.Transport.ReportState("idle");
        _ = _dependencies.Transport.WriteAsync(
            BridgeMessagingUtilities.MakeResultMessageNode(_dependencies.SessionId),
            cancellationToken);

        var status = await _dependencies.ArchiveSessionAsync(_dependencies.SessionId, cancellationToken);
        _dependencies.Transport.Close();
        _dependencies.OnDebug?.Invoke($"[remote-bridge] Torn down (archive={status})");
        ClawSharpTelemetry.LogEvent(
            "tengu_bridge_repl_teardown",
            new Dictionary<string, object?>
            {
                ["archive_status"] = status,
                ["v2"] = true,
                ["session_id"] = _dependencies.SessionId
            });
        ClawSharpTelemetry.RecordMetric("remote_bridge.teardown.count");
    }

    private async Task FlushHistoryAsync(
        IReadOnlyList<RemoteSessionOutboundMessage> messages,
        CancellationToken cancellationToken)
    {
        var eligible = messages
            .Where(message =>
                BridgeMessagingUtilities.IsEligibleBridgeMessage(
                    new BridgeMessageEnvelope(message.Type, message.IsVirtual, message.Subtype)))
            .ToList();

        var capped = _dependencies.InitialHistoryCap > 0 && eligible.Count > _dependencies.InitialHistoryCap
            ? eligible.Skip(eligible.Count - _dependencies.InitialHistoryCap).ToList()
            : eligible;

        if (capped.Count < eligible.Count)
        {
            _dependencies.OnDebug?.Invoke(
                $"[remote-bridge] Capped initial flush: {eligible.Count} -> {capped.Count} (cap={_dependencies.InitialHistoryCap})");
        }

        if (capped.Count == 0)
        {
            return;
        }

        if (capped.Count > 0)
        {
            _dependencies.Transport.ReportState("running");
        }

        _dependencies.OnDebug?.Invoke($"[remote-bridge] Flushing {capped.Count} history events");
        await _dependencies.Transport.WriteBatchAsync(
            capped.Select(message => AttachSessionId(message.SdkEvent)).ToArray(),
            cancellationToken);
        DrainFlushGate();
    }

    private void DrainFlushGate()
    {
        var messages = _flushGate.End();
        if (messages.Count == 0)
        {
            return;
        }

        foreach (var message in messages)
        {
            _recentPostedUuids.Add(message.Uuid);
        }

        if (messages.Any(message => message.Type == "user"))
        {
            _dependencies.Transport.ReportState("running");
        }

        _dependencies.OnDebug?.Invoke(
            $"[remote-bridge] Drained {messages.Count} queued message(s) after flush");
        _ = _dependencies.Transport.WriteBatchAsync(
            messages.Select(message => AttachSessionId(message.SdkEvent)).ToArray());
    }

    private JsonObject AttachSessionId(JsonObject message)
    {
        var clone = message.DeepClone()!.AsObject();
        clone["session_id"] = _dependencies.SessionId;
        return clone;
    }
}
