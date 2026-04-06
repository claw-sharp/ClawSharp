// TS origin: ./utils/fileHistory.ts
using System.Text.Json.Serialization;

namespace ClawSharp.Core;

public sealed record FileHistorySnapshot(
    [property: JsonPropertyName("messageId")] string MessageId,
    [property: JsonPropertyName("trackedFileBackups")] IReadOnlyDictionary<string, FileHistoryBackup> TrackedFileBackups,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp);
