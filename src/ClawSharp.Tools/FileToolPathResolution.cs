using ClawSharp.Core;

namespace ClawSharp.Tools;

internal static class FileToolPathResolution
{
    public static string ExpandLeadingHomePath(string inputPath)
    {
        return PathUtilities.NormalizePathInputForCurrentPlatform(inputPath);
    }

    public static IReadOnlyList<string> GetPathsForPermissionCheck(string inputPath)
    {
        var absolutePath = Path.GetFullPath(ExpandLeadingHomePath(inputPath));
        var pathSet = new HashSet<string>(GetPathComparer())
        {
            absolutePath
        };

        if (IsUncPath(absolutePath))
        {
            return [.. pathSet];
        }

        var root = Path.GetPathRoot(absolutePath);
        if (string.IsNullOrEmpty(root))
        {
            return [.. pathSet];
        }

        var currentPath = root;
        var relativeSegments = absolutePath[root.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index < relativeSegments.Length; index++)
        {
            currentPath = Path.Combine(currentPath, relativeSegments[index]);

            var chainTargets = GetSymlinkChainTargets(currentPath);
            if (chainTargets.Count == 0)
            {
                continue;
            }

            var remainingSegments = relativeSegments[(index + 1)..];
            foreach (var chainTarget in chainTargets)
            {
                pathSet.Add(AppendRemainingSegments(chainTarget, remainingSegments));
            }

            currentPath = chainTargets[^1];
        }

        return [.. pathSet];
    }

    private static List<string> GetSymlinkChainTargets(string path)
    {
        var chainTargets = new List<string>();
        var visited = new HashSet<string>(GetPathComparer());
        var currentPath = path;

        for (var depth = 0; depth < 40; depth++)
        {
            if (!TryGetImmediateSymlinkTarget(currentPath, out var targetPath) || string.IsNullOrWhiteSpace(targetPath))
            {
                break;
            }

            var absoluteTarget = Path.GetFullPath(targetPath);
            if (!visited.Add(absoluteTarget))
            {
                break;
            }

            chainTargets.Add(absoluteTarget);
            currentPath = absoluteTarget;
        }

        return chainTargets;
    }

    private static bool TryGetImmediateSymlinkTarget(string path, out string? targetPath)
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

    private static string AppendRemainingSegments(string basePath, IReadOnlyList<string> remainingSegments)
    {
        if (remainingSegments.Count == 0)
        {
            return Path.GetFullPath(basePath);
        }

        var combinedPath = basePath;
        foreach (var segment in remainingSegments)
        {
            combinedPath = Path.Combine(combinedPath, segment);
        }

        return Path.GetFullPath(combinedPath);
    }

    private static bool IsUncPath(string path)
    {
        return path.StartsWith("\\\\", StringComparison.Ordinal) ||
               path.StartsWith("//", StringComparison.Ordinal);
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }
}
