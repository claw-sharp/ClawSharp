// TS origin: ./tools/FileReadTool/FileReadTool.ts, ./utils/file.ts
using System.Text;

namespace ClawSharp.Tools;

internal static class ReadToolPathHints
{
    public const string FileNotFoundCwdNote = "Note: your current working directory is";

    private const char ThinSpace = (char)8239;

    public static string? GetAlternateScreenshotPath(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var extension = Path.GetExtension(fileName);
        if (!string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (!stem.EndsWith(" AM", StringComparison.Ordinal) &&
            !stem.EndsWith(" PM", StringComparison.Ordinal) &&
            !stem.EndsWith($"{ThinSpace}AM", StringComparison.Ordinal) &&
            !stem.EndsWith($"{ThinSpace}PM", StringComparison.Ordinal))
        {
            return null;
        }

        if (fileName.Contains(ThinSpace))
        {
            return filePath.Replace(ThinSpace, ' ');
        }

        return filePath.Replace(" AM", $"{ThinSpace}AM", StringComparison.Ordinal)
            .Replace(" PM", $"{ThinSpace}PM", StringComparison.Ordinal);
    }

    public static string? FindSimilarFile(string filePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return null;
            }

            var fileBaseName = Path.GetFileNameWithoutExtension(filePath);
            foreach (var entry in Directory.EnumerateFiles(directory))
            {
                if (string.Equals(Path.GetFileNameWithoutExtension(entry), fileBaseName, GetPathComparison()) &&
                    !string.Equals(Path.GetFullPath(entry), Path.GetFullPath(filePath), GetPathComparison()))
                {
                    return Path.GetFileName(entry);
                }
            }
        }
        catch
        {
        }

        return null;
    }

    public static string? SuggestPathUnderWorkspaceRoot(string requestedPath, string workspaceRoot)
    {
        var absoluteWorkspaceRoot = Path.GetFullPath(workspaceRoot);
        var parentDirectory = Path.GetDirectoryName(absoluteWorkspaceRoot);
        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            return null;
        }

        var absoluteRequestedPath = Path.GetFullPath(requestedPath);
        if (!IsWithinRoot(absoluteRequestedPath, parentDirectory) ||
            IsWithinRoot(absoluteRequestedPath, absoluteWorkspaceRoot) ||
            string.Equals(absoluteRequestedPath, absoluteWorkspaceRoot, GetPathComparison()))
        {
            return null;
        }

        var relativeFromParent = Path.GetRelativePath(parentDirectory, absoluteRequestedPath);
        var correctedPath = Path.Combine(absoluteWorkspaceRoot, relativeFromParent);
        return File.Exists(correctedPath) || Directory.Exists(correctedPath)
            ? correctedPath
            : null;
    }

    public static string BuildFileNotFoundMessage(string requestedPath, string workspaceRoot)
    {
        var builder = new StringBuilder();
        builder.Append("File does not exist. ")
            .Append(FileNotFoundCwdNote)
            .Append(' ')
            .Append(workspaceRoot)
            .Append('.');

        var cwdSuggestion = SuggestPathUnderWorkspaceRoot(requestedPath, workspaceRoot);
        if (!string.IsNullOrWhiteSpace(cwdSuggestion))
        {
            builder.Append(" Did you mean ").Append(cwdSuggestion).Append('?');
            return builder.ToString();
        }

        var similarFile = FindSimilarFile(requestedPath);
        if (!string.IsNullOrWhiteSpace(similarFile))
        {
            builder.Append(" Did you mean ").Append(similarFile).Append('?');
        }

        return builder.ToString();
    }

    private static bool IsWithinRoot(string path, string root)
    {
        var normalizedPath = Path.GetFullPath(path);
        var normalizedRoot = Path.GetFullPath(root);
        return string.Equals(normalizedPath, normalizedRoot, GetPathComparison()) ||
               normalizedPath.StartsWith(normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, GetPathComparison()) ||
               normalizedPath.StartsWith(normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.AltDirectorySeparatorChar, GetPathComparison());
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }
}
