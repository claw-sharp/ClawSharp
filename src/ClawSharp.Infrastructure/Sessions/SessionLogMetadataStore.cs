using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class SessionLogMetadataStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<IReadOnlyList<SessionLog>> LoadProjectLogsAsync(
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        var storageDirectory = SessionStoragePaths.GetProjectDir(projectDirectory);
        if (!Directory.Exists(storageDirectory))
        {
            return [];
        }

        var transcriptPaths = Directory.GetFiles(storageDirectory, "*.jsonl", SearchOption.TopDirectoryOnly)
            .Where(IsSessionTranscriptPath)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
        var logs = new List<SessionLog>(transcriptPaths.Length);

        foreach (var transcriptPath in transcriptPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sessionId = Path.GetFileNameWithoutExtension(transcriptPath);
            var metadataPath = SessionStoragePaths.GetSessionLogMetadataPath(projectDirectory, sessionId);
            var transcriptModified = File.GetLastWriteTimeUtc(transcriptPath);
            var log = await LoadFromMetadataAsync(
                metadataPath,
                projectDirectory,
                transcriptPath,
                transcriptModified,
                cancellationToken);
            if (log is null)
            {
                log = await BuildFromTranscriptAsync(projectDirectory, transcriptPath, transcriptModified, cancellationToken);
                await UpsertAsync(log, cancellationToken);
            }

            logs.Add(log);
        }

        return logs
            .OrderByDescending(log => log.Modified)
            .ToArray();
    }

    public Task UpsertAsync(
        ConversationSession session,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var transcriptPath = session.TranscriptPath;
        var modified = File.Exists(transcriptPath)
            ? File.GetLastWriteTimeUtc(transcriptPath)
            : DateTimeOffset.UtcNow;
        var log = new SessionLog(
            session.Id,
            transcriptPath,
            session.ProjectDirectory,
            modified,
            string.IsNullOrWhiteSpace(session.CustomTitle) ? null : session.CustomTitle.Trim(),
            FindFirstUserMessage(messages));
        return UpsertAsync(log, cancellationToken);
    }

    public async Task UpsertAsync(
        SessionLog log,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var metadataPath = SessionStoragePaths.GetSessionLogMetadataPath(log.ProjectDirectory, log.SessionId);
        var metadataDirectory = Path.GetDirectoryName(metadataPath);
        if (!string.IsNullOrWhiteSpace(metadataDirectory))
        {
            Directory.CreateDirectory(metadataDirectory);
        }

        var payload = new SessionLogMetadataDto(
            log.SessionId,
            log.TranscriptPath,
            log.ProjectDirectory,
            log.Modified,
            log.CustomTitle,
            log.FirstUserMessage);
        var tempPath = $"{metadataPath}.{Guid.NewGuid():N}.tmp";
        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        await File.WriteAllTextAsync(tempPath, json, cancellationToken);
        File.Move(tempPath, metadataPath, overwrite: true);
    }

    private static async Task<SessionLog?> LoadFromMetadataAsync(
        string metadataPath,
        string projectDirectory,
        string transcriptPath,
        DateTimeOffset transcriptModified,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
            var metadata = JsonSerializer.Deserialize<SessionLogMetadataDto>(json, SerializerOptions);
            if (metadata is null ||
                string.IsNullOrWhiteSpace(metadata.SessionId) ||
                string.IsNullOrWhiteSpace(metadata.TranscriptPath) ||
                !File.Exists(transcriptPath))
            {
                return null;
            }

            var metadataTranscriptPath = Path.GetFullPath(metadata.TranscriptPath);
            var normalizedTranscriptPath = Path.GetFullPath(transcriptPath);
            if (!string.Equals(metadataTranscriptPath, normalizedTranscriptPath, GetPathComparison()))
            {
                return null;
            }

            return metadata.Modified < transcriptModified
                ? null
                : new SessionLog(
                    metadata.SessionId,
                    normalizedTranscriptPath,
                    string.IsNullOrWhiteSpace(metadata.ProjectDirectory)
                        ? projectDirectory
                        : metadata.ProjectDirectory,
                    metadata.Modified,
                    metadata.CustomTitle,
                    metadata.FirstUserMessage);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<SessionLog> BuildFromTranscriptAsync(
        string projectDirectory,
        string transcriptPath,
        DateTimeOffset modified,
        CancellationToken cancellationToken)
    {
        var sessionId = Path.GetFileNameWithoutExtension(transcriptPath);
        var lines = await File.ReadAllLinesAsync(transcriptPath, cancellationToken);

        string? customTitle = null;
        string? firstUserMessage = null;

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var entry = JsonNode.Parse(line) as JsonObject;
            if (entry is null)
            {
                continue;
            }

            var type = entry["type"]?.GetValue<string>();
            if (type == "custom-title")
            {
                customTitle = entry["customTitle"]?.GetValue<string>() ?? customTitle;
                continue;
            }

            if (type != "user" || firstUserMessage is not null)
            {
                continue;
            }

            firstUserMessage = TryExtractFirstUserMessage(entry);
        }

        return new SessionLog(
            sessionId,
            transcriptPath,
            projectDirectory,
            modified,
            customTitle,
            firstUserMessage);
    }

    private static string? FindFirstUserMessage(IReadOnlyList<ChatMessage> messages)
    {
        foreach (var message in messages)
        {
            if (message.Role != MessageRole.User)
            {
                continue;
            }

            var text = message.ContentBlocks
                .FirstOrDefault(static block => block.Kind == MessageContentKind.Text)
                ?.Value;
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static string? TryExtractFirstUserMessage(JsonObject entry)
    {
        var message = entry["message"] as JsonObject;
        var content = message?["content"] as JsonArray;
        if (content is null)
        {
            return null;
        }

        foreach (var item in content)
        {
            if (item is not JsonObject block)
            {
                continue;
            }

            if (!string.Equals(block["type"]?.GetValue<string>(), "text", StringComparison.Ordinal))
            {
                continue;
            }

            var text = block["text"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static bool IsSessionTranscriptPath(string path)
    {
        return Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _);
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    private sealed record SessionLogMetadataDto(
        string SessionId,
        string TranscriptPath,
        string ProjectDirectory,
        DateTimeOffset Modified,
        string? CustomTitle,
        string? FirstUserMessage);
}
