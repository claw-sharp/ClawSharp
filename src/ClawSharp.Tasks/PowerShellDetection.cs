namespace ClawSharp.Tasks;

public static class PowerShellDetection
{
    private static readonly Lock CacheLock = new();
    private static Task<string?>? _cachedPowerShellPathTask;

    public static Task<string?> GetCachedPowerShellPathAsync()
    {
        lock (CacheLock)
        {
            _cachedPowerShellPathTask ??= FindPowerShellAsync();
            return _cachedPowerShellPathTask;
        }
    }

    public static void ResetPowerShellCache()
    {
        lock (CacheLock)
        {
            _cachedPowerShellPathTask = null;
        }
    }

    public static async Task<string?> FindPowerShellAsync()
    {
        var pwshPath = await FindOnPathAsync("pwsh");
        if (!string.IsNullOrWhiteSpace(pwshPath))
        {
            if (OperatingSystem.IsLinux())
            {
                return await PreferDirectLinuxPwshBinaryIfSnapLauncherAsync(
                    pwshPath,
                    ResolveSymlinkChain,
                    ProbePathAsync);
            }

            return pwshPath;
        }

        return await FindOnPathAsync("powershell");
    }

    public static async Task<PowerShellEdition?> GetPowerShellEditionAsync()
    {
        var path = await GetCachedPowerShellPathAsync();
        return InferEdition(path);
    }

    public static PowerShellEdition? InferEdition(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var fileName = Path.GetFileName(path)
            .ToLowerInvariant()
            .Replace(".exe", string.Empty, StringComparison.Ordinal);

        return fileName == "pwsh"
            ? PowerShellEdition.Core
            : PowerShellEdition.Desktop;
    }

    internal static async Task<string?> FindOnPathAsync(string executableName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (var entry in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(entry, executableName);
            var found = await ProbePathAsync(candidate);
            if (!string.IsNullOrWhiteSpace(found))
            {
                return found;
            }

            if (!OperatingSystem.IsWindows())
            {
                continue;
            }

            var windowsCandidate = Path.Combine(entry, executableName + ".exe");
            found = await ProbePathAsync(windowsCandidate);
            if (!string.IsNullOrWhiteSpace(found))
            {
                return found;
            }
        }

        return null;
    }

    internal static Task<string?> ProbePathAsync(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return Task.FromResult<string?>(path);
            }
        }
        catch
        {
        }

        return Task.FromResult<string?>(null);
    }

    private static async Task<string> PreferDirectLinuxPwshBinaryIfSnapLauncherAsync(
        string pwshPath,
        Func<string, string> resolvePath,
        Func<string, Task<string?>> probePathAsync)
    {
        var resolved = resolvePath(pwshPath);
        if (!pwshPath.StartsWith("/snap/", StringComparison.Ordinal) &&
            !resolved.StartsWith("/snap/", StringComparison.Ordinal))
        {
            return pwshPath;
        }

        var direct =
            await probePathAsync("/opt/microsoft/powershell/7/pwsh") ??
            await probePathAsync("/usr/bin/pwsh");
        if (string.IsNullOrWhiteSpace(direct))
        {
            return pwshPath;
        }

        var directResolved = resolvePath(direct);
        if (direct.StartsWith("/snap/", StringComparison.Ordinal) ||
            directResolved.StartsWith("/snap/", StringComparison.Ordinal))
        {
            return pwshPath;
        }

        return direct;
    }

    private static string ResolveSymlinkChain(string path)
    {
        var currentPath = path;

        for (var depth = 0; depth < 40; depth++)
        {
            if (!TryGetImmediateLinkTarget(currentPath, out var targetPath) || string.IsNullOrWhiteSpace(targetPath))
            {
                break;
            }

            currentPath = targetPath;
        }

        return TryResolveFullPath(currentPath);
    }

    private static bool TryGetImmediateLinkTarget(string path, out string? targetPath)
    {
        targetPath = null;

        if (TryGetLinkTarget(new FileInfo(path), out targetPath) ||
            TryGetLinkTarget(new DirectoryInfo(path), out targetPath))
        {
            return true;
        }

        return false;
    }

    private static bool TryGetLinkTarget(FileSystemInfo info, out string? targetPath)
    {
        targetPath = null;

        try
        {
            var rawTarget = info.LinkTarget;
            if (string.IsNullOrWhiteSpace(rawTarget))
            {
                return false;
            }

            targetPath = Path.IsPathRooted(rawTarget)
                ? rawTarget
                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(info.FullName) ?? string.Empty, rawTarget));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string TryResolveFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }
}
