using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public sealed class McpNeedsAuthCacheTests
{
    [Fact]
    public async Task SetEntryAsync_MarksServerAsCached()
    {
        using var environment = new ClaudeConfigDirectoryScope();
        var now = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        var cache = new McpNeedsAuthCache(() => now);

        await cache.SetEntryAsync("server-one");

        Assert.True(await cache.IsCachedAsync("server-one"));
        Assert.True(File.Exists(cache.GetCachePath()));
    }

    [Fact]
    public async Task IsCachedAsync_ExpiresEntriesAfterTtl()
    {
        using var environment = new ClaudeConfigDirectoryScope();
        var current = new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero);
        var cache = new McpNeedsAuthCache(() => current);
        await cache.SetEntryAsync("server-one");

        current = current.AddMilliseconds(McpNeedsAuthCache.TtlMilliseconds + 1);

        Assert.False(await cache.IsCachedAsync("server-one"));
    }

    [Fact]
    public async Task ClearAsync_RemovesCacheFileAndEntries()
    {
        using var environment = new ClaudeConfigDirectoryScope();
        var cache = new McpNeedsAuthCache(() => new DateTimeOffset(2026, 4, 1, 12, 0, 0, TimeSpan.Zero));
        await cache.SetEntryAsync("server-one");

        await cache.ClearAsync();

        Assert.False(File.Exists(cache.GetCachePath()));
        Assert.False(await cache.IsCachedAsync("server-one"));
    }

    private sealed class ClaudeConfigDirectoryScope : IDisposable
    {
        private readonly string? _original;

        public ClaudeConfigDirectoryScope()
        {
            _original = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "clawsharp-mcp-auth-cache-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", path);
            Path = path;
        }

        public string Path { get; }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", _original);
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
