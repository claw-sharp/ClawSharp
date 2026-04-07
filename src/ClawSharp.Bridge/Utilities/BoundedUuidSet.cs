namespace ClawSharp.Bridge;

public sealed class BoundedUuidSet
{
    private readonly int _capacity;
    private readonly string?[] _ring;
    private readonly HashSet<string> _set = new(StringComparer.Ordinal);
    private int _writeIndex;

    public BoundedUuidSet(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        }

        _capacity = capacity;
        _ring = new string?[capacity];
    }

    public void Add(string uuid)
    {
        ArgumentNullException.ThrowIfNull(uuid);

        if (_set.Contains(uuid))
        {
            return;
        }

        var evicted = _ring[_writeIndex];
        if (evicted is not null)
        {
            _set.Remove(evicted);
        }

        _ring[_writeIndex] = uuid;
        _set.Add(uuid);
        _writeIndex = (_writeIndex + 1) % _capacity;
    }

    public bool Contains(string uuid)
    {
        ArgumentNullException.ThrowIfNull(uuid);
        return _set.Contains(uuid);
    }

    public void Clear()
    {
        _set.Clear();
        Array.Fill(_ring, null);
        _writeIndex = 0;
    }
}
