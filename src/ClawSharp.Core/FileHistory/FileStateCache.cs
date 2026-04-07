using System.Text;

namespace ClawSharp.Core;

public sealed class FileStateCache
{
    public const int DefaultMaxEntries = 100;
    public const long DefaultMaxSizeBytes = 25L * 1024 * 1024;

    private readonly object _gate = new();
    private readonly int _maxEntries;
    private readonly long _maxSizeBytes;
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _entries;
    private readonly LinkedList<CacheEntry> _lru = new();
    private long _calculatedSizeBytes;

    public FileStateCache(int maxEntries, long maxSizeBytes = DefaultMaxSizeBytes)
    {
        if (maxEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntries));
        }

        if (maxSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSizeBytes));
        }

        _maxEntries = maxEntries;
        _maxSizeBytes = maxSizeBytes;
        _entries = new Dictionary<string, LinkedListNode<CacheEntry>>(StringComparer.Ordinal);
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public int MaxEntries => _maxEntries;

    public long MaxSizeBytes => _maxSizeBytes;

    public long CalculatedSizeBytes
    {
        get
        {
            lock (_gate)
            {
                return _calculatedSizeBytes;
            }
        }
    }

    public FileState? Get(string key)
    {
        var normalizedKey = NormalizeKey(key);

        lock (_gate)
        {
            if (!_entries.TryGetValue(normalizedKey, out var node))
            {
                return null;
            }

            Touch(node);
            return node.Value.Value;
        }
    }

    public FileStateCache Set(string key, FileState value)
    {
        var normalizedKey = NormalizeKey(key);

        lock (_gate)
        {
            if (_entries.TryGetValue(normalizedKey, out var existing))
            {
                _calculatedSizeBytes -= existing.Value.SizeBytes;
                existing.Value = new CacheEntry(normalizedKey, value, GetEntrySizeBytes(value));
                _calculatedSizeBytes += existing.Value.SizeBytes;
                Touch(existing);
            }
            else
            {
                var node = new LinkedListNode<CacheEntry>(
                    new CacheEntry(normalizedKey, value, GetEntrySizeBytes(value)));
                _lru.AddFirst(node);
                _entries[normalizedKey] = node;
                _calculatedSizeBytes += node.Value.SizeBytes;
            }

            TrimToLimits();
            return this;
        }
    }

    public bool Has(string key)
    {
        var normalizedKey = NormalizeKey(key);

        lock (_gate)
        {
            return _entries.ContainsKey(normalizedKey);
        }
    }

    public bool Delete(string key)
    {
        var normalizedKey = NormalizeKey(key);

        lock (_gate)
        {
            if (!_entries.Remove(normalizedKey, out var node))
            {
                return false;
            }

            _lru.Remove(node);
            _calculatedSizeBytes -= node.Value.SizeBytes;
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _lru.Clear();
            _calculatedSizeBytes = 0;
        }
    }

    public IReadOnlyList<string> Keys()
    {
        lock (_gate)
        {
            return _lru.Select(entry => entry.Key).ToArray();
        }
    }

    public IReadOnlyList<KeyValuePair<string, FileState>> Entries()
    {
        lock (_gate)
        {
            return _lru
                .Select(entry => new KeyValuePair<string, FileState>(entry.Key, entry.Value))
                .ToArray();
        }
    }

    public IReadOnlyList<KeyValuePair<string, FileState>> Dump()
    {
        return Entries();
    }

    public void Load(IEnumerable<KeyValuePair<string, FileState>> entries)
    {
        Clear();

        foreach (var entry in entries)
        {
            Set(entry.Key, entry.Value);
        }
    }

    public static FileStateCache CreateWithSizeLimit(int maxEntries, long maxSizeBytes = DefaultMaxSizeBytes)
    {
        return new FileStateCache(maxEntries, maxSizeBytes);
    }

    public static FileStateCache Clone(FileStateCache cache)
    {
        var cloned = CreateWithSizeLimit(cache.MaxEntries, cache.MaxSizeBytes);
        cloned.Load(cache.Dump());
        return cloned;
    }

    public static FileStateCache Merge(FileStateCache first, FileStateCache second)
    {
        var merged = Clone(first);
        foreach (var (filePath, fileState) in second.Entries())
        {
            var existing = merged.Get(filePath);
            if (existing is null || fileState.Timestamp > existing.Timestamp)
            {
                merged.Set(filePath, fileState);
            }
        }

        return merged;
    }

    private static string NormalizeKey(string key)
    {
        return Path.GetFullPath(key);
    }

    private static long GetEntrySizeBytes(FileState value)
    {
        return Math.Max(1, Encoding.UTF8.GetByteCount(value.Content));
    }

    private void Touch(LinkedListNode<CacheEntry> node)
    {
        if (!ReferenceEquals(_lru.First, node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
        }
    }

    private void TrimToLimits()
    {
        while (_entries.Count > _maxEntries || _calculatedSizeBytes > _maxSizeBytes)
        {
            var last = _lru.Last;
            if (last is null)
            {
                break;
            }

            _lru.RemoveLast();
            _entries.Remove(last.Value.Key);
            _calculatedSizeBytes -= last.Value.SizeBytes;
        }
    }

    private sealed record CacheEntry(
        string Key,
        FileState Value,
        long SizeBytes);
}
