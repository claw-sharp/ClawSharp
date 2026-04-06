namespace ClawSharp.Bridge;

public interface ICapacityWake
{
    CapacitySignal CreateSignal();

    void Wake();
}

public readonly record struct CapacitySignal(
    CancellationToken Token,
    Action Cleanup);

public sealed class CapacityWake : ICapacityWake
{
    private readonly CancellationToken _outerToken;
    private CancellationTokenSource _wakeSource = new();
    private readonly object _gate = new();

    public CapacityWake(CancellationToken outerToken)
    {
        _outerToken = outerToken;
    }

    public void Wake()
    {
        CancellationTokenSource previous;

        lock (_gate)
        {
            previous = _wakeSource;
            _wakeSource = new CancellationTokenSource();
        }

        previous.Cancel();
        previous.Dispose();
    }

    public CapacitySignal CreateSignal()
    {
        CancellationTokenSource merged;
        CancellationTokenSource currentWakeSource;

        lock (_gate)
        {
            currentWakeSource = _wakeSource;
        }

        if (_outerToken.IsCancellationRequested || currentWakeSource.IsCancellationRequested)
        {
            merged = new CancellationTokenSource();
            merged.Cancel();
            return new CapacitySignal(
                merged.Token,
                () => merged.Dispose());
        }

        merged = CancellationTokenSource.CreateLinkedTokenSource(_outerToken, currentWakeSource.Token);

        return new CapacitySignal(
            merged.Token,
            () => merged.Dispose());
    }
}
