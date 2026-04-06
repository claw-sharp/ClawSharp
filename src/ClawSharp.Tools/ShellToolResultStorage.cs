using ClawSharp.Core;
using ClawSharp.Tasks;

namespace ClawSharp.Tools;

internal sealed record PersistedShellOutput(
    string FilePath,
    long OriginalSize);

internal static class ShellToolResultStorage
{
    private const string ToolResultsSubdir = "tool-results";
    private const string PersistedOutputTag = "<persisted-output>";
    private const string PersistedOutputClosingTag = "</persisted-output>";
    private const int PreviewSizeBytes = 2000;
    private const long MaxPersistedSizeBytes = 64L * 1024 * 1024;

    public static async Task<PersistedShellOutput?> TryPersistLargeOutputAsync(
        ConversationSession session,
        string outputFilePath,
        string outputTaskId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputFilePath) || string.IsNullOrWhiteSpace(outputTaskId))
        {
            return null;
        }

        try
        {
            var fileInfo = new FileInfo(outputFilePath);
            if (!fileInfo.Exists)
            {
                return null;
            }

            var toolResultsDir = GetToolResultsDir(session);
            Directory.CreateDirectory(toolResultsDir);
            var destination = Path.Combine(toolResultsDir, $"{outputTaskId}.txt");

            if (fileInfo.Length > MaxPersistedSizeBytes)
            {
                await CopyCappedAsync(outputFilePath, destination, MaxPersistedSizeBytes, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                File.Copy(outputFilePath, destination, overwrite: true);
            }

            return new PersistedShellOutput(destination, fileInfo.Length);
        }
        catch
        {
            return null;
        }
    }

    public static string BuildLargeToolResultMessage(string previewSource, PersistedShellOutput persistedOutput)
    {
        var preview = GeneratePreview(previewSource, PreviewSizeBytes);
        return
            $"{PersistedOutputTag}\n" +
            $"Output too large ({FormatFileSize(persistedOutput.OriginalSize)}). Full output saved to: {persistedOutput.FilePath}\n\n" +
            $"Preview (first {FormatFileSize(PreviewSizeBytes)}):\n" +
            preview.Preview +
            (preview.HasMore ? "\n...\n" : "\n") +
            PersistedOutputClosingTag;
    }

    public static (string Preview, bool HasMore) GeneratePreview(string content, int maxBytes)
    {
        if (content.Length <= maxBytes)
        {
            return (content, false);
        }

        var truncated = content[..maxBytes];
        var lastNewline = truncated.LastIndexOf('\n');
        var cutPoint = lastNewline > maxBytes / 2
            ? lastNewline
            : maxBytes;
        return (content[..cutPoint], true);
    }

    public static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        var kilobytes = bytes / 1024d;
        if (kilobytes < 1024)
        {
            return $"{kilobytes:0.#} KB";
        }

        var megabytes = kilobytes / 1024d;
        if (megabytes < 1024)
        {
            return $"{megabytes:0.#} MB";
        }

        var gigabytes = megabytes / 1024d;
        return $"{gigabytes:0.#} GB";
    }

    private static string GetToolResultsDir(ConversationSession session)
    {
        return Path.Combine(
            SessionStoragePaths.GetProjectDir(session.ProjectDirectory),
            session.Id,
            ToolResultsSubdir);
    }

    private static async Task CopyCappedAsync(
        string sourcePath,
        string destinationPath,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 4096,
            useAsync: true);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        var remaining = maxBytes;
        var buffer = new byte[81920];
        while (remaining > 0)
        {
            var bytesToRead = (int)Math.Min(buffer.Length, remaining);
            var read = await source.ReadAsync(buffer.AsMemory(0, bytesToRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            remaining -= read;
        }
    }
}
