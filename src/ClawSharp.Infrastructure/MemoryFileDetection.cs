// TS origin: ./utils/memoryFileDetection.ts
using System.Text.RegularExpressions;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public static partial class MemoryFileDetection
{
    [GeneratedRegex(@"(?:[A-Za-z]:[/\\]|/)[^\s'""]+", RegexOptions.CultureInvariant)]
    private static partial Regex AbsolutePathTokenRegex();

    public static string? DetectSessionFileType(string filePath, string? configHomeDir = null)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        var configDir = configHomeDir ?? SessionStoragePaths.GetClaudeConfigHomeDir();
        var normalized = ToComparable(filePath);
        var configDirComparable = ToComparable(configDir);
        if (!normalized.StartsWith(configDirComparable, GetComparison()))
        {
            return null;
        }

        if (normalized.Contains("/session-memory/", GetComparison()) &&
            normalized.EndsWith(".md", GetComparison()))
        {
            return "session_memory";
        }

        if (normalized.Contains("/projects/", GetComparison()) &&
            normalized.EndsWith(".jsonl", GetComparison()))
        {
            return "session_transcript";
        }

        return null;
    }

    public static string? DetectSessionPatternType(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        var normalized = ToPosix(pattern);
        if (normalized.Contains("session-memory", StringComparison.Ordinal) &&
            (normalized.Contains(".md", StringComparison.Ordinal) || normalized.EndsWith('*')))
        {
            return "session_memory";
        }

        if (normalized.Contains(".jsonl", StringComparison.Ordinal) ||
            (normalized.Contains("projects", StringComparison.Ordinal) &&
             normalized.Contains("*.jsonl", StringComparison.Ordinal)))
        {
            return "session_transcript";
        }

        return null;
    }

    public static bool IsAutoMemFile(string filePath, string autoMemoryDirectory)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(autoMemoryDirectory);

        if (string.IsNullOrWhiteSpace(autoMemoryDirectory))
        {
            return false;
        }

        var fileComparable = ToComparable(filePath);
        var autoMemComparable = ToComparable(autoMemoryDirectory);
        return fileComparable.StartsWith(autoMemComparable, GetComparison());
    }

    public static bool IsAutoManagedMemoryFile(
        string filePath,
        string autoMemoryDirectory,
        string? configHomeDir = null)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(autoMemoryDirectory);

        if (IsAutoMemFile(filePath, autoMemoryDirectory))
        {
            return true;
        }

        return DetectSessionFileType(filePath, configHomeDir) is not null;
    }

    public static bool IsMemoryDirectory(
        string directoryPath,
        string memoryBaseDirectory,
        string? autoMemoryDirectory = null,
        string? configHomeDir = null)
    {
        ArgumentNullException.ThrowIfNull(directoryPath);
        ArgumentNullException.ThrowIfNull(memoryBaseDirectory);

        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(directoryPath);
        }
        catch
        {
            normalizedPath = directoryPath;
        }

        var normalizedComparable = ToComparable(normalizedPath);
        if (!string.IsNullOrWhiteSpace(autoMemoryDirectory))
        {
            var autoMemComparable = ToComparable(autoMemoryDirectory.TrimEnd('/', '\\'));
            var autoMemPrefix = ToComparable(autoMemoryDirectory);
            if (normalizedComparable.Equals(autoMemComparable, GetComparison()) ||
                normalizedComparable.StartsWith(autoMemPrefix, GetComparison()))
            {
                return true;
            }
        }

        var configDirComparable = ToComparable(configHomeDir ?? SessionStoragePaths.GetClaudeConfigHomeDir());
        var memoryBaseComparable = ToComparable(memoryBaseDirectory);
        var underConfig = normalizedComparable.StartsWith(configDirComparable, GetComparison());
        var underMemoryBase = normalizedComparable.StartsWith(memoryBaseComparable, GetComparison());
        if (!underConfig && !underMemoryBase)
        {
            return false;
        }

        if (normalizedComparable.Contains("/session-memory/", GetComparison()))
        {
            return true;
        }

        if (underConfig && normalizedComparable.Contains("/projects/", GetComparison()))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(autoMemoryDirectory) &&
               underMemoryBase &&
               normalizedComparable.Contains("/memory/", GetComparison());
    }

    public static bool IsShellCommandTargetingMemory(
        string command,
        string memoryBaseDirectory,
        string? autoMemoryDirectory = null,
        string? configHomeDir = null)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(memoryBaseDirectory);

        var configDir = configHomeDir ?? SessionStoragePaths.GetClaudeConfigHomeDir();
        var commandComparable = ToComparable(command);
        var candidateDirectories = new List<string> { configDir, memoryBaseDirectory };
        if (!string.IsNullOrWhiteSpace(autoMemoryDirectory))
        {
            candidateDirectories.Add(autoMemoryDirectory);
        }

        var mentionsTrackedDirectory = candidateDirectories.Any(
            directory =>
            {
                if (commandComparable.Contains(ToComparable(directory), GetComparison()))
                {
                    return true;
                }

                if (OperatingSystem.IsWindows())
                {
                    return commandComparable.Contains(
                        ToComparable(WindowsPathConversion.WindowsPathToPosixPath(directory)),
                        GetComparison());
                }

                return false;
            });
        if (!mentionsTrackedDirectory)
        {
            return false;
        }

        foreach (Match match in AbsolutePathTokenRegex().Matches(command))
        {
            var cleanPath = match.Value.TrimEnd(',', ';', '|', '&', '>');
            var nativePath = OperatingSystem.IsWindows()
                ? PathUtilities.NormalizePathInputForCurrentPlatform(cleanPath)
                : cleanPath;
            if (IsAutoManagedMemoryFile(nativePath, autoMemoryDirectory ?? string.Empty, configDir) ||
                IsMemoryDirectory(nativePath, memoryBaseDirectory, autoMemoryDirectory, configDir))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsAutoManagedMemoryPattern(string pattern)
    {
        return IsAutoManagedMemoryPattern(pattern, autoMemoryEnabled: false);
    }

    public static bool IsAutoManagedMemoryPattern(string pattern, bool autoMemoryEnabled)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        if (DetectSessionPatternType(pattern) is not null)
        {
            return true;
        }

        var normalized = pattern.Replace('\\', '/');
        return autoMemoryEnabled &&
               (normalized.Contains("agent-memory/", StringComparison.Ordinal) ||
                normalized.Contains("agent-memory-local/", StringComparison.Ordinal));
    }

    private static string ToComparable(string path)
    {
        var posixForm = ToPosix(path);
        return OperatingSystem.IsWindows()
            ? posixForm.ToLowerInvariant()
            : posixForm;
    }

    private static string ToPosix(string path)
    {
        return path.Replace('\\', '/');
    }

    private static StringComparison GetComparison()
    {
        return OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }
}
