namespace ClawSharp.Contracts.Review;

public sealed record FileDiff(
    string FilePath,
    IReadOnlyList<DiffHunk> Hunks);
