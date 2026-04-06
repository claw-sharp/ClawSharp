// TS origin: ./utils/fileHistory.ts
using System.Text.Json.Serialization;

namespace ClawSharp.Core;

public sealed record FileHistoryBackup(
    [property: JsonPropertyName("backupFileName")] string? BackupFileName,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("backupTime")] DateTimeOffset BackupTime);
