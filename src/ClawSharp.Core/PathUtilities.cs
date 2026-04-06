using System.Text;
using System.Text.RegularExpressions;

namespace ClawSharp.Core;

public static partial class PathUtilities
{
    [GeneratedRegex(@"^/cygdrive/([A-Za-z])(\/|$)")]
    private static partial Regex CygdrivePathRegex();

    [GeneratedRegex(@"^/([A-Za-z])(\/|$)")]
    private static partial Regex PosixDrivePathRegex();

    public static string ExpandPath(string path, string? baseDir = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        var actualBaseDir = baseDir ?? Directory.GetCurrentDirectory();
        ArgumentNullException.ThrowIfNull(actualBaseDir);

        if (path.Contains('\0') || actualBaseDir.Contains('\0'))
        {
            throw new ArgumentException("Path contains null bytes");
        }

        var trimmedPath = path.Trim();
        if (trimmedPath.Length == 0)
        {
            return NormalizeFormC(Path.GetFullPath(actualBaseDir));
        }

        var processedPath = NormalizePathInputForCurrentPlatform(trimmedPath);
        if (Path.IsPathRooted(processedPath))
        {
            return NormalizeFormC(Path.GetFullPath(processedPath));
        }

        return NormalizeFormC(Path.GetFullPath(Path.Combine(actualBaseDir, processedPath)));
    }

    public static string NormalizePathInputForCurrentPlatform(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var expanded = ExpandHomeDirectoryPrefix(path);
        if (!OperatingSystem.IsWindows())
        {
            return expanded;
        }

        return ConvertPosixWindowsAbsolutePath(expanded);
    }

    public static string NormalizePathForConfigKey(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var normalizedInput = NormalizePathInputForCurrentPlatform(path);
        return NormalizeFormC(Path.GetFullPath(normalizedInput))
            .Replace('\\', '/');
    }

    public static string ResolveRealPathLikeNode(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var absolutePath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(absolutePath);
        if (string.IsNullOrEmpty(root))
        {
            return absolutePath;
        }

        var currentPath = root;
        var relativeSegments = absolutePath[root.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index < relativeSegments.Length; index++)
        {
            currentPath = Path.Combine(currentPath, relativeSegments[index]);

            var resolvedTarget = ResolveSymlinkChainTarget(currentPath);
            if (resolvedTarget is null)
            {
                continue;
            }

            currentPath = resolvedTarget;
        }

        return NormalizeFormC(Path.GetFullPath(currentPath));
    }

    private static string ExpandHomeDirectoryPrefix(string path)
    {
        if (path == "~" ||
            path.StartsWith("~/", StringComparison.Ordinal) ||
            (OperatingSystem.IsWindows() && path.StartsWith("~\\", StringComparison.Ordinal)))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var relativePath = path.Length == 1
                ? string.Empty
                : path[2..];
            return relativePath.Length == 0
                ? home
                : Path.Combine(home, relativePath);
        }

        return path;
    }

    private static string ConvertPosixWindowsAbsolutePath(string path)
    {
        if (path.StartsWith("//", StringComparison.Ordinal))
        {
            return path.Replace('/', '\\');
        }

        var cygdriveMatch = CygdrivePathRegex().Match(path);
        if (cygdriveMatch.Success)
        {
            var driveLetter = char.ToUpperInvariant(cygdriveMatch.Groups[1].Value[0]);
            var rest = path[("/cygdrive/" + cygdriveMatch.Groups[1].Value).Length..];
            return driveLetter + ":" + (rest.Length == 0 ? @"\" : rest.Replace('/', '\\'));
        }

        var driveMatch = PosixDrivePathRegex().Match(path);
        if (driveMatch.Success)
        {
            var driveLetter = char.ToUpperInvariant(driveMatch.Groups[1].Value[0]);
            var rest = path[2..];
            return driveLetter + ":" + (rest.Length == 0 ? @"\" : rest.Replace('/', '\\'));
        }

        return path;
    }

    private static string NormalizeFormC(string path)
    {
        return path.Normalize(NormalizationForm.FormC);
    }

    private static string? ResolveSymlinkChainTarget(string path)
    {
        var currentPath = path;
        var visited = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        if (!visited.Add(currentPath))
        {
            return null;
        }

        for (var depth = 0; depth < 40; depth++)
        {
            if (!TryGetImmediateLinkTarget(currentPath, out var targetPath) || string.IsNullOrWhiteSpace(targetPath))
            {
                break;
            }

            currentPath = NormalizeFormC(Path.GetFullPath(targetPath));
            if (!visited.Add(currentPath))
            {
                break;
            }
        }

        return string.Equals(currentPath, path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            ? null
            : currentPath;
    }

    private static bool TryGetImmediateLinkTarget(string path, out string? targetPath)
    {
        targetPath = null;

        return TryGetLinkTarget(new FileInfo(path), out targetPath) ||
               TryGetLinkTarget(new DirectoryInfo(path), out targetPath);
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
}
