// TS origin: ./services/mcp/client.ts
using System.Text.Json;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class McpNeedsAuthCache
{
    public const long TtlMilliseconds = 15 * 60 * 1000;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private Task<Dictionary<string, McpNeedsAuthCacheEntry>>? _cacheTask;

    public McpNeedsAuthCache(Func<DateTimeOffset>? now = null)
    {
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public string GetCachePath()
    {
        return Path.Combine(SessionStoragePaths.GetClaudeConfigHomeDir(), "mcp-needs-auth-cache.json");
    }

    public async Task<bool> IsCachedAsync(string serverId, CancellationToken cancellationToken = default)
    {
        var cache = await GetCacheAsync(cancellationToken).ConfigureAwait(false);
        if (!cache.TryGetValue(serverId, out var entry))
        {
            return false;
        }

        return _now().ToUnixTimeMilliseconds() - entry.Timestamp < TtlMilliseconds;
    }

    public async Task SetEntryAsync(string serverId, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cache = await GetCacheAsync(cancellationToken).ConfigureAwait(false);
            cache[serverId] = new McpNeedsAuthCacheEntry(_now().ToUnixTimeMilliseconds());
            var cachePath = GetCachePath();
            var directory = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(cache, SerializerOptions), cancellationToken).ConfigureAwait(false);
            _cacheTask = null;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _cacheTask = null;
        var cachePath = GetCachePath();
        if (File.Exists(cachePath))
        {
            File.Delete(cachePath);
        }

        return Task.CompletedTask;
    }

    private Task<Dictionary<string, McpNeedsAuthCacheEntry>> GetCacheAsync(CancellationToken cancellationToken)
    {
        _cacheTask ??= ReadCacheAsync(cancellationToken);
        return _cacheTask;
    }

    private async Task<Dictionary<string, McpNeedsAuthCacheEntry>> ReadCacheAsync(CancellationToken cancellationToken)
    {
        var cachePath = GetCachePath();
        if (!File.Exists(cachePath))
        {
            return new Dictionary<string, McpNeedsAuthCacheEntry>(StringComparer.Ordinal);
        }

        try
        {
            var json = await File.ReadAllTextAsync(cachePath, cancellationToken).ConfigureAwait(false);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, McpNeedsAuthCacheEntry>>(json, SerializerOptions);
            return parsed is null
                ? new Dictionary<string, McpNeedsAuthCacheEntry>(StringComparer.Ordinal)
                : new Dictionary<string, McpNeedsAuthCacheEntry>(parsed, StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, McpNeedsAuthCacheEntry>(StringComparer.Ordinal);
        }
    }
}
