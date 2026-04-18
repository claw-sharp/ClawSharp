namespace ClawSharp.Contracts.Projects;

public sealed record ProjectSummary(
    string Id,
    string Name,
    string Path,
    string? GitBranch,
    int ThreadCount,
    DateTimeOffset LastUpdatedAt);
