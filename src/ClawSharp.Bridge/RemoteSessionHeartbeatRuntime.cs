namespace ClawSharp.Bridge;

public interface IRemoteSessionHeartbeatRuntime : IAsyncDisposable
{
    void Start();
}

public sealed record RemoteSessionHeartbeatRuntimeDependencies(
    BridgeHeartbeatDependencies HeartbeatDependencies,
    BridgeTransportReconnectState State,
    EnvLessBridgeConfig Config,
    Func<double, CancellationToken, Task>? SleepAsync = null,
    Func<double>? NextRandomDouble = null,
    Action<string>? OnDebug = null);

public sealed class RemoteSessionHeartbeatRuntime : IRemoteSessionHeartbeatRuntime
{
    private readonly RemoteSessionHeartbeatRuntimeDependencies _dependencies;
    private readonly object _gate = new();
    private CancellationTokenSource? _lifetimeCancellation;
    private Task? _loopTask;
    private bool _disposed;

    public RemoteSessionHeartbeatRuntime(RemoteSessionHeartbeatRuntimeDependencies dependencies)
    {
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _loopTask is not null || _dependencies.Config.HeartbeatIntervalMs <= 0)
            {
                return;
            }

            _lifetimeCancellation = new CancellationTokenSource();
            _loopTask = RunAsync(_lifetimeCancellation.Token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? lifetimeCancellation;
        Task? loopTask;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            lifetimeCancellation = _lifetimeCancellation;
            loopTask = _loopTask;
            _lifetimeCancellation = null;
            _loopTask = null;
        }

        if (lifetimeCancellation is not null)
        {
            lifetimeCancellation.Cancel();
            lifetimeCancellation.Dispose();
        }

        if (loopTask is null)
        {
            return;
        }

        try
        {
            await loopTask;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var sleepAsync = _dependencies.SleepAsync
                         ?? ((delayMs, token) => Task.Delay(TimeSpan.FromMilliseconds(delayMs), token));
        var nextRandomDouble = _dependencies.NextRandomDouble ?? Random.Shared.NextDouble;

        _dependencies.OnDebug?.Invoke("[bridge:heartbeat] Started remote session heartbeat runtime");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await sleepAsync(GetNextDelayMs(nextRandomDouble), cancellationToken);

                var heartbeatInfo = GetHeartbeatInfo();
                if (heartbeatInfo is null)
                {
                    continue;
                }

                await BridgeHeartbeatCoordinator.TryHeartbeatCurrentWorkItemAsync(
                    _dependencies.HeartbeatDependencies,
                    heartbeatInfo,
                    cancellationToken);
            }
        }
        finally
        {
            _dependencies.OnDebug?.Invoke("[bridge:heartbeat] Stopped remote session heartbeat runtime");
        }
    }

    private double GetNextDelayMs(Func<double> nextRandomDouble)
    {
        var intervalMs = _dependencies.Config.HeartbeatIntervalMs;
        var jitter = intervalMs * _dependencies.Config.HeartbeatJitterFraction * (2d * nextRandomDouble() - 1d);
        return Math.Max(0d, intervalMs + jitter);
    }

    private BridgeHeartbeatInfo? GetHeartbeatInfo()
    {
        var state = _dependencies.State;
        if (string.IsNullOrWhiteSpace(state.EnvironmentId) ||
            string.IsNullOrWhiteSpace(state.CurrentWorkId) ||
            string.IsNullOrWhiteSpace(state.CurrentIngressToken))
        {
            return null;
        }

        return new BridgeHeartbeatInfo(
            state.EnvironmentId,
            state.CurrentWorkId,
            state.CurrentIngressToken);
    }
}
