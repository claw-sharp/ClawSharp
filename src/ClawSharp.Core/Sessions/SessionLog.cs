namespace ClawSharp.Core;

public sealed record SessionLog(
    string SessionId,
    string TranscriptPath,
    string ProjectDirectory,
    DateTimeOffset Modified,
    string? CustomTitle,
    string? FirstUserMessage);
