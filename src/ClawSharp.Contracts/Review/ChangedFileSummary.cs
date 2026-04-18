namespace ClawSharp.Contracts.Review;

public sealed record ChangedFileSummary(
    string Path,
    string Status,
    int Additions,
    int Deletions,
    string ThreadId);
