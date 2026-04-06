// TS origin: ./bridge/flushGate.ts
using System.Collections.ObjectModel;

namespace ClawSharp.Bridge;

public sealed class FlushGate<T>
{
    private readonly List<T> _pending = [];
    private bool _active;

    public bool Active => _active;

    public int PendingCount => _pending.Count;

    public void Start()
    {
        _active = true;
    }

    public IReadOnlyList<T> End()
    {
        _active = false;
        if (_pending.Count == 0)
        {
            return Array.Empty<T>();
        }

        var drained = _pending.ToArray();
        _pending.Clear();
        return new ReadOnlyCollection<T>(drained);
    }

    public bool Enqueue(params T[] items)
    {
        if (!_active)
        {
            return false;
        }

        foreach (var item in items)
        {
            _pending.Add(item);
        }

        return true;
    }

    public int Drop()
    {
        _active = false;
        var count = _pending.Count;
        _pending.Clear();
        return count;
    }

    public void Deactivate()
    {
        _active = false;
    }
}
