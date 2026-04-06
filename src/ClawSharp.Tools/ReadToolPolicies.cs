// TS origin: ./tools/FileReadTool/FileReadTool.ts, ./tools/FileReadTool/prompt.ts, ./tools/FileReadTool/limits.ts, ./constants/files.ts, ./utils/file.ts
namespace ClawSharp.Tools;

internal static class ReadToolPolicies
{
    public const int MaxLinesToRead = 2000;
    public const int MaxReturnedCharacters = 4000;
    public const long MaxSizeBytes = 256 * 1024;
    public const long ImageTargetRawSizeBytes = (5 * 1024 * 1024 * 3) / 4;
    public const long PdfTargetRawSizeBytes = 20 * 1024 * 1024;
    public const long PdfMaxExtractSizeBytes = 100 * 1024 * 1024;
    public const int PdfMaxPagesPerRead = 20;
    public const int PdfAtMentionInlineThreshold = 10;

    public const string FileUnchangedStub =
        "File unchanged since last read. The content from the earlier Read tool_result in this conversation is still current — refer to that instead of re-reading.";

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".tiff", ".tif",
        ".mp4", ".mov", ".avi", ".mkv", ".webm", ".wmv", ".flv", ".m4v", ".mpeg", ".mpg",
        ".mp3", ".wav", ".ogg", ".flac", ".aac", ".m4a", ".wma", ".aiff", ".opus",
        ".zip", ".tar", ".gz", ".bz2", ".7z", ".rar", ".xz", ".z", ".tgz", ".iso",
        ".exe", ".dll", ".so", ".dylib", ".bin", ".o", ".a", ".obj", ".lib", ".app", ".msi", ".deb", ".rpm",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp",
        ".ttf", ".otf", ".woff", ".woff2", ".eot",
        ".pyc", ".pyo", ".class", ".jar", ".war", ".ear", ".node", ".wasm", ".rlib",
        ".sqlite", ".sqlite3", ".db", ".mdb", ".idx",
        ".psd", ".ai", ".eps", ".sketch", ".fig", ".xd", ".blend", ".3ds", ".max",
        ".swf", ".fla",
        ".lockb", ".dat", ".data"
    };

    private static readonly HashSet<string> BlockedDevicePaths = new(StringComparer.Ordinal)
    {
        "/dev/zero",
        "/dev/random",
        "/dev/urandom",
        "/dev/full",
        "/dev/stdin",
        "/dev/tty",
        "/dev/console",
        "/dev/stdout",
        "/dev/stderr",
        "/dev/fd/0",
        "/dev/fd/1",
        "/dev/fd/2"
    };

    private static readonly HashSet<string> AllowedBinaryReadExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".ipynb"
    };

    public static bool HasBlockedBinaryExtension(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return BinaryExtensions.Contains(extension) && !AllowedBinaryReadExtensions.Contains(extension);
    }

    public static bool IsBlockedDevicePath(string filePath)
    {
        if (BlockedDevicePaths.Contains(filePath))
        {
            return true;
        }

        return filePath.StartsWith("/proc/", StringComparison.Ordinal) &&
               (filePath.EndsWith("/fd/0", StringComparison.Ordinal) ||
                filePath.EndsWith("/fd/1", StringComparison.Ordinal) ||
                filePath.EndsWith("/fd/2", StringComparison.Ordinal));
    }

    public static bool IsPdfExtension(string filePath)
    {
        return string.Equals(Path.GetExtension(filePath), ".pdf", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParsePdfPageRange(string pages, out PdfPageRange? range)
    {
        range = null;
        var trimmed = pages.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        if (trimmed.EndsWith("-", StringComparison.Ordinal))
        {
            if (!int.TryParse(trimmed[..^1], out var firstPage) || firstPage < 1)
            {
                return false;
            }

            range = new PdfPageRange(firstPage, int.MaxValue);
            return true;
        }

        var dashIndex = trimmed.IndexOf('-', StringComparison.Ordinal);
        if (dashIndex < 0)
        {
            if (!int.TryParse(trimmed, out var singlePage) || singlePage < 1)
            {
                return false;
            }

            range = new PdfPageRange(singlePage, singlePage);
            return true;
        }

        if (!int.TryParse(trimmed[..dashIndex], out var rangeStart) ||
            !int.TryParse(trimmed[(dashIndex + 1)..], out var rangeEnd) ||
            rangeStart < 1 ||
            rangeEnd < 1 ||
            rangeEnd < rangeStart)
        {
            return false;
        }

        range = new PdfPageRange(rangeStart, rangeEnd);
        return true;
    }
}

public sealed record PdfPageRange(int FirstPage, int LastPage);
