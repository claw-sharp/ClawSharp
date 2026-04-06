// TS origin: ./utils/settings/settingsCache.ts
using System.Collections.Concurrent;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class SettingsCache
{
    private ClawSharpSettings? _mergedSessionCache;
    private readonly ConcurrentDictionary<string, ClawSharpSettings> _perSourceCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ParsedSettings> _parseFileCache = new(StringComparer.Ordinal);

    public ClawSharpSettings? MergedSessionCache
    {
        get => _mergedSessionCache;
        set => _mergedSessionCache = value;
    }

    public ClawSharpSettings? GetCachedSettingsForSource(string source)
    {
        return _perSourceCache.TryGetValue(source, out var settings) ? settings : null;
    }

    public void SetCachedSettingsForSource(string source, ClawSharpSettings settings)
    {
        _perSourceCache[source] = settings;
    }

    public ParsedSettings? GetCachedParsedFile(string path)
    {
        return _parseFileCache.TryGetValue(path, out var parsed) ? parsed : null;
    }

    public void SetCachedParsedFile(string path, ParsedSettings parsed)
    {
        _parseFileCache[path] = parsed;
    }

    public void Reset()
    {
        _mergedSessionCache = null;
        _perSourceCache.Clear();
        _parseFileCache.Clear();
    }
}

public sealed record ParsedSettings(
    ClawSharpSettings? Settings,
    IReadOnlyList<SettingsLoadIssue> Issues);
