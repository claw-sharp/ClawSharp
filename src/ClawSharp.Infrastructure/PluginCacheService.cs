// TS origin: ./utils/plugins/zipCache.ts
using System.IO.Compression;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class PluginCacheService : IDisposable
{
    private string? _sessionPluginCachePath;
    private readonly object _lock = new();

    public bool IsPluginZipCacheEnabled()
    {
        var enabled = Environment.GetEnvironmentVariable("CLAUDE_CODE_PLUGIN_USE_ZIP_CACHE");
        return !string.IsNullOrEmpty(enabled) && (enabled == "1" || enabled.Equals("true", StringComparison.OrdinalIgnoreCase));
    }

    public string? GetPluginZipCachePath()
    {
        if (!IsPluginZipCacheEnabled()) return null;
        var dir = Environment.GetEnvironmentVariable("CLAUDE_CODE_PLUGIN_CACHE_DIR");
        return string.IsNullOrEmpty(dir) ? null : Path.GetFullPath(dir);
    }

    public async Task<string> GetSessionPluginCachePathAsync()
    {
        if (_sessionPluginCachePath != null) return _sessionPluginCachePath;

        lock (_lock)
        {
            if (_sessionPluginCachePath != null) return _sessionPluginCachePath;
            
            var suffix = Guid.NewGuid().ToString("n").Substring(0, 16);
            var dir = Path.Combine(Path.GetTempPath(), $"claude-plugin-session-{suffix}");
            Directory.CreateDirectory(dir);
            _sessionPluginCachePath = dir;
            return dir;
        }
    }

    public async Task ExtractZipToDirectoryAsync(string zipPath, string targetDir, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(zipPath)) throw new FileNotFoundException("Plugin ZIP file not found.", zipPath);

        Directory.CreateDirectory(targetDir);
        
        // Wait, ZipFile.ExtractToDirectory is synchronous in many versions of .NET.
        // We'll run it in a Task.Run to keep it async-friendly.
        await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, targetDir, overwriteFiles: true), cancellationToken);
    }

    public async Task ConvertDirectoryToZipInPlaceAsync(string dirPath, string zipPath, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => 
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(dirPath, zipPath);
            Directory.Delete(dirPath, recursive: true);
        }, cancellationToken);
    }

    public void Dispose()
    {
        if (_sessionPluginCachePath != null && Directory.Exists(_sessionPluginCachePath))
        {
            try { Directory.Delete(_sessionPluginCachePath, recursive: true); } catch { /* Ignore cleanup errors */ }
        }
    }
}
