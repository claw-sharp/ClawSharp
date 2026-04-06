namespace ClawSharp.Core;

public sealed record FileHistoryDiffStats(
    IReadOnlyList<string> FilesChanged,
    int Insertions,
    int Deletions);
