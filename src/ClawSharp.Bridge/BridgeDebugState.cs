using ClawSharp.Core;

namespace ClawSharp.Bridge;

public sealed record BridgeFault(
    string Method,
    string Kind,
    int Status,
    string? ErrorType = null,
    int Count = 1);

public interface IBridgeDebugHandle
{
    void FireClose(int code);

    void ForceReconnect();

    void InjectFault(BridgeFault fault);

    void WakePollLoop();

    string Describe();
}

public static class BridgeDebugState
{
    private static readonly object SyncRoot = new();
    private static readonly List<BridgeFault> FaultQueue = [];
    private static IBridgeDebugHandle? _debugHandle;

    public static void RegisterBridgeDebugHandle(IBridgeDebugHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);

        lock (SyncRoot)
        {
            _debugHandle = handle;
        }
    }

    public static void ClearBridgeDebugHandle()
    {
        lock (SyncRoot)
        {
            _debugHandle = null;
            FaultQueue.Clear();
        }
    }

    public static IBridgeDebugHandle? GetBridgeDebugHandle()
    {
        lock (SyncRoot)
        {
            return _debugHandle;
        }
    }

    public static void InjectBridgeFault(BridgeFault fault)
    {
        ArgumentNullException.ThrowIfNull(fault);

        lock (SyncRoot)
        {
            FaultQueue.Add(fault);
        }

        ClawSharpTelemetry.LogDebug(
            $"[bridge:debug] Queued fault: {fault.Method} {fault.Kind}/{fault.Status}{(fault.ErrorType is null ? string.Empty : $"/{fault.ErrorType}")} x{fault.Count}");
        ClawSharpTelemetry.LogEvent(
            "tengu_bridge_fault_injected",
            new Dictionary<string, object?>
            {
                ["method"] = fault.Method,
                ["kind"] = fault.Kind,
                ["status"] = fault.Status,
                ["count"] = fault.Count,
                ["error_type"] = fault.ErrorType
            });
    }

    public static BridgeFault? TryConsumeFault(string method)
    {
        ArgumentNullException.ThrowIfNull(method);

        lock (SyncRoot)
        {
            var index = FaultQueue.FindIndex(fault => string.Equals(fault.Method, method, StringComparison.Ordinal));
            if (index < 0)
            {
                return null;
            }

            var fault = FaultQueue[index];
            var remaining = fault.Count - 1;
            if (remaining <= 0)
            {
                FaultQueue.RemoveAt(index);
            }
            else
            {
                FaultQueue[index] = fault with { Count = remaining };
            }

            return fault;
        }
    }

    public static IBridgeApiClient WrapApiForFaultInjection(
        IBridgeApiClient api,
        Action<string>? onDebug = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        return new FaultInjectingBridgeApiClient(api, onDebug);
    }

    private sealed class FaultInjectingBridgeApiClient : IBridgeApiClient
    {
        private readonly IBridgeApiClient _inner;
        private readonly Action<string>? _onDebug;

        public FaultInjectingBridgeApiClient(IBridgeApiClient inner, Action<string>? onDebug)
        {
            _inner = inner;
            _onDebug = onDebug;
        }

        public Task<BridgeRegisterEnvironmentResponse> RegisterBridgeEnvironmentAsync(BridgeConfig config, CancellationToken cancellationToken = default)
        {
            ThrowIfInjected("registerBridgeEnvironment", "Registration");
            return _inner.RegisterBridgeEnvironmentAsync(config, cancellationToken);
        }

        public Task<BridgeWorkResponse?> PollForWorkAsync(string environmentId, string environmentSecret, CancellationToken cancellationToken = default, int? reclaimOlderThanMs = null)
        {
            ThrowIfInjected("pollForWork", "Poll");
            return _inner.PollForWorkAsync(environmentId, environmentSecret, cancellationToken, reclaimOlderThanMs);
        }

        public Task AcknowledgeWorkAsync(string environmentId, string workId, string sessionToken, CancellationToken cancellationToken = default)
        {
            return _inner.AcknowledgeWorkAsync(environmentId, workId, sessionToken, cancellationToken);
        }

        public Task StopWorkAsync(string environmentId, string workId, bool force, CancellationToken cancellationToken = default)
        {
            return _inner.StopWorkAsync(environmentId, workId, force, cancellationToken);
        }

        public Task DeregisterEnvironmentAsync(string environmentId, CancellationToken cancellationToken = default)
        {
            return _inner.DeregisterEnvironmentAsync(environmentId, cancellationToken);
        }

        public Task SendPermissionResponseEventAsync(string sessionId, PermissionResponseEvent @event, string sessionToken, CancellationToken cancellationToken = default)
        {
            return _inner.SendPermissionResponseEventAsync(sessionId, @event, sessionToken, cancellationToken);
        }

        public Task ArchiveSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            return _inner.ArchiveSessionAsync(sessionId, cancellationToken);
        }

        public Task ReconnectSessionAsync(string environmentId, string sessionId, CancellationToken cancellationToken = default)
        {
            ThrowIfInjected("reconnectSession", "ReconnectSession");
            return _inner.ReconnectSessionAsync(environmentId, sessionId, cancellationToken);
        }

        public Task<BridgeHeartbeatResponse> HeartbeatWorkAsync(string environmentId, string workId, string sessionToken, CancellationToken cancellationToken = default)
        {
            ThrowIfInjected("heartbeatWork", "Heartbeat");
            return _inner.HeartbeatWorkAsync(environmentId, workId, sessionToken, cancellationToken);
        }

        private void ThrowIfInjected(string method, string context)
        {
            var fault = TryConsumeFault(method);
            if (fault is null)
            {
                return;
            }

            _onDebug?.Invoke(
                $"[bridge:debug] Injecting {fault.Kind} fault into {context}: status={fault.Status} errorType={fault.ErrorType ?? "none"}");
            ClawSharpTelemetry.LogDebug(
                $"[bridge:debug] Injecting {fault.Kind} fault into {context}: status={fault.Status} errorType={fault.ErrorType ?? "none"}");

            if (string.Equals(fault.Kind, "fatal", StringComparison.Ordinal))
            {
                throw new BridgeFatalError(
                    $"[injected] {context} {fault.Status}",
                    fault.Status,
                    fault.ErrorType);
            }

            throw new InvalidOperationException($"[injected transient] {context} {fault.Status}");
        }
    }
}
