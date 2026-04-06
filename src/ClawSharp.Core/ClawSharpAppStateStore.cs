// TS origin: ./state/store.ts
namespace ClawSharp.Core;

public sealed class ClawSharpAppStateStore : IClawSharpAppStateStore
{
    private readonly Lock _lock = new();
    private readonly HashSet<Action> _listeners = [];
    private readonly Action<ClawSharpAppState, ClawSharpAppState>? _onChange;
    private ClawSharpAppState _state;

    public ClawSharpAppStateStore(
        ClawSharpAppState initialState,
        Action<ClawSharpAppState, ClawSharpAppState>? onChange = null)
    {
        _state = initialState;
        _onChange = onChange;
    }

    public ClawSharpAppState GetState()
    {
        lock (_lock)
        {
            return _state;
        }
    }

    public void SetState(Func<ClawSharpAppState, ClawSharpAppState> updater)
    {
        Action[] listeners;
        ClawSharpAppState previousState;
        ClawSharpAppState nextState;

        lock (_lock)
        {
            previousState = _state;
            nextState = updater(previousState);
            if (ReferenceEquals(nextState, previousState))
            {
                return;
            }

            _state = nextState;
            listeners = _listeners.ToArray();
        }

        _onChange?.Invoke(nextState, previousState);
        foreach (var listener in listeners)
        {
            listener();
        }
    }

    public IDisposable Subscribe(Action listener)
    {
        lock (_lock)
        {
            _listeners.Add(listener);
        }

        return new Subscription(this, listener);
    }

    private void Unsubscribe(Action listener)
    {
        lock (_lock)
        {
            _listeners.Remove(listener);
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly ClawSharpAppStateStore _store;
        private readonly Action _listener;
        private bool _disposed;

        public Subscription(ClawSharpAppStateStore store, Action listener)
        {
            _store = store;
            _listener = listener;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _store.Unsubscribe(_listener);
        }
    }
}
