namespace ClawSharp.Contracts.Review;

public sealed record DiffLine(
    string Type,
    string Content,
    int? OldLineNumber = null,
    int? NewLineNumber = null);
