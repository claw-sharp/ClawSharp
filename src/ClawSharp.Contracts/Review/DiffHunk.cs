namespace ClawSharp.Contracts.Review;

public sealed record DiffHunk(
    string Header,
    IReadOnlyList<DiffLine> Lines);
