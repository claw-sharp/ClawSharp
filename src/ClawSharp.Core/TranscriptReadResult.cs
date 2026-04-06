namespace ClawSharp.Core;

public sealed record TranscriptReadResult(
    IReadOnlyList<ChatMessage> Messages,
    string? CustomTitle,
    FileHistoryState FileHistoryState);
