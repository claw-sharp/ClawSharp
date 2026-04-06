// TS origin: ./utils/permissions/filesystem.ts
using ClawSharp.Core;

namespace ClawSharp.Tools;

internal static class FileToolAutoEditSafety
{
    private static readonly string[] DangerousFiles =
    [
        ".gitconfig",
        ".gitmodules",
        ".bashrc",
        ".bash_profile",
        ".zshrc",
        ".zprofile",
        ".profile",
        ".ripgreprc",
        ".claude.json"
    ];

    private static readonly string[] DangerousDirectories =
    [
        ".git",
        ".vscode",
        ".idea",
        ".claude"
    ];

    public static FileToolAutoEditSafetyResult Check(
        string originalPath,
        string workspaceRoot,
        IReadOnlyList<string> pathsToCheck)
    {
        foreach (var pathToCheck in EnumeratePathsToCheck(originalPath, pathsToCheck))
        {
            if (HasSuspiciousWindowsPathPattern(pathToCheck))
            {
                return FileToolAutoEditSafetyResult.Blocked(
                    $"Claude requested permissions to write to {originalPath}, which contains a suspicious Windows path pattern that requires manual approval.");
            }
        }

        foreach (var pathToCheck in EnumeratePathsToCheck(originalPath, pathsToCheck))
        {
            if (IsClaudeConfigFilePath(pathToCheck, workspaceRoot))
            {
                return FileToolAutoEditSafetyResult.Blocked(
                    $"Claude requested permissions to write to {originalPath}, but you haven't granted it yet.");
            }
        }

        foreach (var pathToCheck in EnumeratePathsToCheck(originalPath, pathsToCheck))
        {
            if (IsDangerousFilePathToAutoEdit(pathToCheck))
            {
                return FileToolAutoEditSafetyResult.Blocked(
                    $"Claude requested permissions to edit {originalPath} which is a sensitive file.");
            }
        }

        return FileToolAutoEditSafetyResult.Allowed();
    }

    private static IEnumerable<string> EnumeratePathsToCheck(string originalPath, IReadOnlyList<string> pathsToCheck)
    {
        yield return originalPath;
        foreach (var pathToCheck in pathsToCheck)
        {
            yield return pathToCheck;
        }
    }

    private static bool IsClaudeConfigFilePath(string filePath, string workspaceRoot)
    {
        var expandedPath = Path.GetFullPath(filePath);
        var normalizedPath = NormalizeCaseForComparison(expandedPath);
        var workspaceClaudeDir = Path.Combine(Path.GetFullPath(workspaceRoot), ".claude");
        var userClaudeDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude");

        if (normalizedPath.EndsWith(
                $"{Path.DirectorySeparatorChar}.claude{Path.DirectorySeparatorChar}settings.json",
                GetPathComparison()) ||
            normalizedPath.EndsWith(
                $"{Path.DirectorySeparatorChar}.claude{Path.DirectorySeparatorChar}settings.local.json",
                GetPathComparison()))
        {
            return true;
        }

        return IsWithinDirectory(expandedPath, Path.Combine(workspaceClaudeDir, "commands")) ||
               IsWithinDirectory(expandedPath, Path.Combine(workspaceClaudeDir, "agents")) ||
               IsWithinDirectory(expandedPath, Path.Combine(workspaceClaudeDir, "skills")) ||
               IsWithinDirectory(expandedPath, Path.Combine(userClaudeDir, "commands")) ||
               IsWithinDirectory(expandedPath, Path.Combine(userClaudeDir, "agents")) ||
               IsWithinDirectory(expandedPath, Path.Combine(userClaudeDir, "skills"));
    }

    private static bool IsDangerousFilePathToAutoEdit(string path)
    {
        var absolutePath = Path.GetFullPath(path);
        var pathSegments = absolutePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fileName = Path.GetFileName(absolutePath);

        if (path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
        {
            return true;
        }

        for (var index = 0; index < pathSegments.Length; index++)
        {
            var segment = pathSegments[index];
            var normalizedSegment = NormalizeCaseForComparison(segment);
            foreach (var dangerousDirectory in DangerousDirectories)
            {
                if (!string.Equals(
                        normalizedSegment,
                        NormalizeCaseForComparison(dangerousDirectory),
                        GetPathComparison()))
                {
                    continue;
                }

                if (dangerousDirectory == ".claude" &&
                    index + 1 < pathSegments.Length &&
                    string.Equals(
                        NormalizeCaseForComparison(pathSegments[index + 1]),
                        "worktrees",
                        GetPathComparison()))
                {
                    break;
                }

                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var normalizedFileName = NormalizeCaseForComparison(fileName);
            foreach (var dangerousFile in DangerousFiles)
            {
                if (string.Equals(
                        normalizedFileName,
                        NormalizeCaseForComparison(dangerousFile),
                        GetPathComparison()))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasSuspiciousWindowsPathPattern(string path)
    {
        if ((OperatingSystem.IsWindows() || IsWsl()) &&
            path.IndexOf(':', 2) >= 0)
        {
            return true;
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(path, "~\\d"))
        {
            return true;
        }

        if (path.StartsWith("\\\\?\\", StringComparison.Ordinal) ||
            path.StartsWith("\\\\.\\", StringComparison.Ordinal) ||
            path.StartsWith("//?/", StringComparison.Ordinal) ||
            path.StartsWith("//./", StringComparison.Ordinal))
        {
            return true;
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(path, @"[.\s]+$"))
        {
            return true;
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(path, @"\.(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            return true;
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(path, @"(^|/|\\)\.{3,}(/|\\|$)"))
        {
            return true;
        }

        if (path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static bool IsWithinDirectory(string path, string directory)
    {
        var absolutePath = Path.GetFullPath(path);
        var absoluteDirectory = Path.GetFullPath(directory);
        return string.Equals(absolutePath, absoluteDirectory, GetPathComparison()) ||
               absolutePath.StartsWith(
                   absoluteDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
                   GetPathComparison()) ||
               absolutePath.StartsWith(
                   absoluteDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.AltDirectorySeparatorChar,
                   GetPathComparison());
    }

    private static string NormalizeCaseForComparison(string path)
    {
        return path.ToLowerInvariant();
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    private static bool IsWsl()
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        try
        {
            var version = File.ReadAllText("/proc/version");
            return version.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ||
                   version.Contains("WSL", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}

internal sealed record FileToolAutoEditSafetyResult(bool Safe, string? Message)
{
    public static FileToolAutoEditSafetyResult Allowed()
    {
        return new FileToolAutoEditSafetyResult(true, null);
    }

    public static FileToolAutoEditSafetyResult Blocked(string message)
    {
        return new FileToolAutoEditSafetyResult(false, message);
    }
}
