using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class JsonlTranscriptStore : ITranscriptStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task RecordTranscriptAsync(
        ConversationSession session,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var newMessages = messages
            .Where(message => !session.HasRecorded(message.Id))
            .ToArray();
        var pendingFileHistorySnapshots = session.DrainPendingFileHistorySnapshots();

        var transcriptPath = session.TranscriptPath;
        var shouldRecordCustomTitle =
            session.HasUnrecordedCustomTitle() &&
            (newMessages.Length > 0 || File.Exists(transcriptPath));

        if (newMessages.Length == 0 && !shouldRecordCustomTitle && pendingFileHistorySnapshots.Count == 0)
        {
            return;
        }

        var transcriptDirectory = Path.GetDirectoryName(transcriptPath);
        if (transcriptDirectory is not null)
        {
            Directory.CreateDirectory(transcriptDirectory);
        }

        await using var stream = new FileStream(
            transcriptPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        await using var writer = new StreamWriter(stream);

        if (shouldRecordCustomTitle)
        {
            await WriteLineAsync(
                writer,
                new CustomTitleTranscriptEntry("custom-title", session.Id, session.CustomTitle!),
                cancellationToken);
            session.MarkCustomTitleRecorded();
        }

        foreach (var fileHistorySnapshot in pendingFileHistorySnapshots)
        {
            await WriteLineAsync(
                writer,
                new FileHistorySnapshotTranscriptEntry(
                    "file-history-snapshot",
                    fileHistorySnapshot.Snapshot.MessageId,
                    fileHistorySnapshot.Snapshot,
                    fileHistorySnapshot.IsSnapshotUpdate),
                cancellationToken);
        }

        foreach (var message in newMessages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteLineAsync(writer, ToTranscriptEntry(message), cancellationToken);
            session.MarkRecorded(message.Id);
        }

        await writer.FlushAsync(cancellationToken);
    }

    public async Task<TranscriptReadResult> ReadTranscriptAsync(
        string transcriptPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(transcriptPath))
        {
            return new TranscriptReadResult([], null, FileHistoryState.Empty);
        }

        var lines = await File.ReadAllLinesAsync(transcriptPath, cancellationToken);
        var messages = new List<ChatMessage>(lines.Length);
        var customTitle = default(string);
        var snapshotEntries = new List<FileHistorySnapshotEntry>();
        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var root = JsonNode.Parse(line) as JsonObject;
            if (root is null)
            {
                continue;
            }

            if (TryParseCustomTitleEntry(root, out var parsedCustomTitle))
            {
                customTitle = parsedCustomTitle;
                continue;
            }

            if (TryParseFileHistorySnapshotEntry(root, out var snapshotEntry))
            {
                snapshotEntries.Add(snapshotEntry);
                continue;
            }

            var message = TryParseTranscriptEntry(root);
            if (message is not null)
            {
                messages.Add(message);
            }
        }

        var fileHistoryState = snapshotEntries.Count == 0
            ? FileHistoryState.Empty
            : RestoreFileHistoryState(snapshotEntries);

        return new TranscriptReadResult(messages, customTitle, fileHistoryState);
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public async Task RecordSessionMetadataAsync(
        ConversationSession session,
        CancellationToken cancellationToken = default)
    {
        if (!session.HasUnrecordedCustomTitle())
        {
            return;
        }

        var transcriptPath = session.TranscriptPath;
        if (!File.Exists(transcriptPath))
        {
            return;
        }

        await using var stream = new FileStream(
            transcriptPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);
        await using var writer = new StreamWriter(stream);
        await WriteLineAsync(
            writer,
            new CustomTitleTranscriptEntry("custom-title", session.Id, session.CustomTitle!),
            cancellationToken);
        await writer.FlushAsync(cancellationToken);
        session.MarkCustomTitleRecorded();
    }

    private static async Task WriteLineAsync(
        StreamWriter writer,
        object entry,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var line = JsonSerializer.Serialize(entry, SerializerOptions);
        await writer.WriteLineAsync(line);
    }

    private static object ToTranscriptEntry(ChatMessage message)
    {
        if (TryCreateProgressTranscriptEntry(message, out var progressEntry) && progressEntry is not null)
        {
            return progressEntry;
        }

        return new TranscriptEntry(
            message.Id,
            message.Role.ToString().ToLowerInvariant(),
            message.Timestamp,
            new SerializedMessage(
                message.Role.ToString().ToLowerInvariant(),
                message.ContentBlocks.Select(ToSerializedContentBlock).ToArray()));
    }

    private static SerializedContentBlock ToSerializedContentBlock(MessageContentBlock block)
    {
        return block.Kind switch
        {
            MessageContentKind.Text => new SerializedContentBlock(
                "text",
                Text: block.Value,
                Metadata: CreateMetadataNode(block.Metadata)),
            MessageContentKind.ToolUse => new SerializedContentBlock(
                "tool_use",
                Name: block.Name,
                Id: TryGetMetadataValue(block, "toolUseId"),
                Input: block.Value,
                Metadata: CreateMetadataNode(block.Metadata)),
            MessageContentKind.ToolResult => new SerializedContentBlock(
                "tool_result",
                Content: block.Value,
                ToolUseId: TryGetMetadataValue(block, "toolUseId"),
                StructuredOutput: ParseJsonValue(TryGetMetadataValue(block, "structuredOutput")),
                Metadata: CreateMetadataNode(block.Metadata)),
            MessageContentKind.Attachment => new SerializedContentBlock(
                "attachment",
                Text: block.Value,
                Name: block.Name,
                Metadata: CreateMetadataNode(block.Metadata)),
            MessageContentKind.Progress => throw new InvalidOperationException("Progress content blocks must be serialized as top-level progress transcript entries."),
            _ => throw new InvalidOperationException($"Unsupported content block kind '{block.Kind}'.")
        };
    }

    private static JsonObject? CreateMetadataNode(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return null;
        }

        var result = new JsonObject();
        foreach (var (key, value) in metadata)
        {
            result[key] = value;
        }

        return result;
    }

    private static bool TryCreateProgressTranscriptEntry(ChatMessage message, out ProgressTranscriptEntry? progressEntry)
    {
        progressEntry = null;
        if (message.ContentBlocks.Count != 1 || message.ContentBlocks[0].Kind != MessageContentKind.Progress)
        {
            return false;
        }

        var block = message.ContentBlocks[0];
        progressEntry = new ProgressTranscriptEntry(
            message.Id,
            "progress",
            message.Timestamp,
            ParseProgressData(block.Value),
            TryGetMetadataValue(block, "toolUseId") ?? string.Empty,
            TryGetMetadataValue(block, "parentToolUseId") ?? string.Empty);
        return true;
    }

    private static JsonNode? ParseProgressData(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return JsonNode.Parse(value);
    }

    private static ChatMessage? TryParseTranscriptEntry(JsonObject entry)
    {
        var type = entry["type"]?.GetValue<string>();
        return type switch
        {
            "progress" => ParseProgressEntry(entry),
            "user" or "assistant" or "system" or "attachment" => ParseMessageEntry(entry),
            _ => null
        };
    }

    private static bool TryParseCustomTitleEntry(JsonObject entry, out string? customTitle)
    {
        customTitle = null;
        if (!string.Equals(entry["type"]?.GetValue<string>(), "custom-title", StringComparison.Ordinal))
        {
            return false;
        }

        customTitle = entry["customTitle"]?.GetValue<string>();
        return true;
    }

    private static bool TryParseFileHistorySnapshotEntry(JsonObject entry, out FileHistorySnapshotEntry snapshotEntry)
    {
        snapshotEntry = default!;
        if (!string.Equals(entry["type"]?.GetValue<string>(), "file-history-snapshot", StringComparison.Ordinal))
        {
            return false;
        }

        var snapshotNode = entry["snapshot"];
        if (snapshotNode is null)
        {
            return false;
        }

        var parsedSnapshot = snapshotNode.Deserialize<FileHistorySnapshot>(SerializerOptions);
        if (parsedSnapshot is null)
        {
            return false;
        }

        snapshotEntry = new FileHistorySnapshotEntry(
            parsedSnapshot,
            entry["isSnapshotUpdate"]?.GetValue<bool>() ?? false);
        return true;
    }

    private static FileHistoryState RestoreFileHistoryState(IReadOnlyList<FileHistorySnapshotEntry> snapshotEntries)
    {
        var snapshots = new List<FileHistorySnapshot>(snapshotEntries.Count);
        var trackedFiles = new HashSet<string>(StringComparer.Ordinal);

        foreach (var snapshotEntry in snapshotEntries)
        {
            if (snapshotEntry.IsSnapshotUpdate)
            {
                var index = snapshots.FindLastIndex(snapshot => string.Equals(
                    snapshot.MessageId,
                    snapshotEntry.Snapshot.MessageId,
                    StringComparison.Ordinal));
                if (index >= 0)
                {
                    snapshots[index] = snapshotEntry.Snapshot;
                }
                else
                {
                    snapshots.Add(snapshotEntry.Snapshot);
                }
            }
            else
            {
                snapshots.Add(snapshotEntry.Snapshot);
            }

            foreach (var trackingPath in snapshotEntry.Snapshot.TrackedFileBackups.Keys)
            {
                trackedFiles.Add(trackingPath);
            }
        }

        return new FileHistoryState(
            snapshots,
            trackedFiles,
            snapshots.Count);
    }

    private static ChatMessage? ParseProgressEntry(JsonObject entry)
    {
        var id = entry["uuid"]?.GetValue<string>();
        var toolUseId = entry["toolUseID"]?.GetValue<string>();
        var parentToolUseId = entry["parentToolUseID"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(id) ||
            string.IsNullOrWhiteSpace(toolUseId) ||
            string.IsNullOrWhiteSpace(parentToolUseId))
        {
            return null;
        }

        var timestamp = ParseTimestamp(entry["timestamp"]);
        var data = entry["data"] as JsonObject ?? [];
        return new ChatMessage(
            id,
            MessageRole.System,
            [
                new MessageContentBlock(
                    MessageContentKind.Progress,
                    data.ToJsonString(),
                    Metadata: new Dictionary<string, string>
                    {
                        ["toolUseId"] = toolUseId,
                        ["parentToolUseId"] = parentToolUseId
                    })
            ],
            timestamp);
    }

    private static ChatMessage? ParseMessageEntry(JsonObject entry)
    {
        var id = entry["uuid"]?.GetValue<string>();
        var messageObject = entry["message"] as JsonObject;
        var role = ParseMessageRole(messageObject?["role"]?.GetValue<string>());
        if (string.IsNullOrWhiteSpace(id) || messageObject is null || role is null)
        {
            return null;
        }

        var contentArray = messageObject["content"] as JsonArray;
        if (contentArray is null)
        {
            return null;
        }

        var blocks = new List<MessageContentBlock>(contentArray.Count);
        foreach (var item in contentArray)
        {
            if (item is not JsonObject blockObject)
            {
                continue;
            }

            var block = ParseContentBlock(blockObject);
            if (block is not null)
            {
                blocks.Add(block);
            }
        }

        return new ChatMessage(
            id,
            role.Value,
            blocks,
            ParseTimestamp(entry["timestamp"]));
    }

    private static MessageContentBlock? ParseContentBlock(JsonObject block)
    {
        var type = block["type"]?.GetValue<string>();
        return type switch
        {
            "text" => new MessageContentBlock(
                MessageContentKind.Text,
                block["text"]?.GetValue<string>() ?? string.Empty,
                Metadata: ParseMetadata(block["metadata"] as JsonObject)),
            "tool_use" => new MessageContentBlock(
                MessageContentKind.ToolUse,
                block["input"]?.GetValue<string>() ?? string.Empty,
                block["name"]?.GetValue<string>(),
                MergeMetadata(
                    ParseMetadata(block["metadata"] as JsonObject),
                    CreateMetadata(("toolUseId", block["id"]?.GetValue<string>())))),
            "tool_result" => new MessageContentBlock(
                MessageContentKind.ToolResult,
                block["content"]?.GetValue<string>() ?? string.Empty,
                Metadata: MergeMetadata(
                    ParseMetadata(block["metadata"] as JsonObject),
                    CreateMetadata(
                        ("toolUseId", block["tool_use_id"]?.GetValue<string>()),
                        ("structuredOutput", block["structured_output"]?.ToJsonString())))),
            "attachment" => new MessageContentBlock(
                MessageContentKind.Attachment,
                block["text"]?.GetValue<string>() ?? string.Empty,
                block["name"]?.GetValue<string>(),
                ParseMetadata(block["metadata"] as JsonObject)),
            _ => null
        };
    }

    private static Dictionary<string, string>? CreateMetadata(params (string Key, string? Value)[] values)
    {
        Dictionary<string, string>? metadata = null;
        foreach (var (key, value) in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            metadata ??= new Dictionary<string, string>(StringComparer.Ordinal);
            metadata[key] = value;
        }

        return metadata;
    }

    private static Dictionary<string, string>? CreateMetadata(
        Dictionary<string, string>? seed,
        params (string Key, string? Value)[] values)
    {
        var metadata = seed is null
            ? null
            : new Dictionary<string, string>(seed, StringComparer.Ordinal);

        foreach (var (key, value) in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            metadata ??= new Dictionary<string, string>(StringComparer.Ordinal);
            metadata[key] = value;
        }

        return metadata;
    }

    private static Dictionary<string, string>? ParseMetadata(JsonObject? node)
    {
        if (node is null || node.Count == 0)
        {
            return null;
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in node)
        {
            if (pair.Value is null)
            {
                continue;
            }

            metadata[pair.Key] = pair.Value.GetValue<string>();
        }

        return metadata.Count == 0 ? null : metadata;
    }

    private static Dictionary<string, string>? MergeMetadata(
        Dictionary<string, string>? first,
        Dictionary<string, string>? second)
    {
        if (first is null)
        {
            return second;
        }

        if (second is null)
        {
            return first;
        }

        var merged = new Dictionary<string, string>(first, StringComparer.Ordinal);
        foreach (var (key, value) in second)
        {
            merged[key] = value;
        }

        return merged;
    }

    private static DateTimeOffset ParseTimestamp(JsonNode? node)
    {
        return node is null
            ? DateTimeOffset.UtcNow
            : node.Deserialize<DateTimeOffset>()!;
    }

    private static MessageRole? ParseMessageRole(string? role)
    {
        return role?.ToLowerInvariant() switch
        {
            "system" => MessageRole.System,
            "user" => MessageRole.User,
            "assistant" => MessageRole.Assistant,
            "tool" => MessageRole.Tool,
            _ => null
        };
    }

    private static JsonNode? ParseJsonValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return JsonNode.Parse(value);
    }

    private static string? TryGetMetadataValue(MessageContentBlock block, string key)
    {
        return block.Metadata is not null && block.Metadata.TryGetValue(key, out var value)
            ? value
            : null;
    }

    private sealed record TranscriptEntry(
        [property: JsonPropertyName("uuid")] string Uuid,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
        [property: JsonPropertyName("message")] SerializedMessage Message);

    private sealed record ProgressTranscriptEntry(
        [property: JsonPropertyName("uuid")] string Uuid,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
        [property: JsonPropertyName("data")] JsonNode? Data,
        [property: JsonPropertyName("toolUseID")] string ToolUseId,
        [property: JsonPropertyName("parentToolUseID")] string ParentToolUseId);

    private sealed record CustomTitleTranscriptEntry(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("sessionId")] string SessionId,
        [property: JsonPropertyName("customTitle")] string CustomTitle);

    private sealed record FileHistorySnapshotTranscriptEntry(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("messageId")] string MessageId,
        [property: JsonPropertyName("snapshot")] FileHistorySnapshot Snapshot,
        [property: JsonPropertyName("isSnapshotUpdate")] bool IsSnapshotUpdate);

    private sealed record SerializedMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] IReadOnlyList<SerializedContentBlock> Content);

    private sealed record SerializedContentBlock(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string? Text = null,
        [property: JsonPropertyName("name")] string? Name = null,
        [property: JsonPropertyName("id")] string? Id = null,
        [property: JsonPropertyName("input")] string? Input = null,
        [property: JsonPropertyName("content")] string? Content = null,
        [property: JsonPropertyName("tool_use_id")] string? ToolUseId = null,
        [property: JsonPropertyName("structured_output")] JsonNode? StructuredOutput = null,
        [property: JsonPropertyName("metadata")] JsonObject? Metadata = null);
}
