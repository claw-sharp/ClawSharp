namespace ClawSharp.AgentHost.Contracts;

public sealed record ProjectSummaryDto(
    string Id,
    string Name,
    string Path,
    DateTimeOffset LastOpenedAt,
    DateTimeOffset? LastUpdatedAt,
    int ThreadCount,
    string? GitBranch);

public sealed record ThreadWorktreeMetadataDto(
    string RepoRoot,
    string? WorktreePath,
    string? WorktreeStatus,
    string? BaseBranch,
    string? BaseCommit);

public sealed record ThreadSummaryDto(
    string Id,
    string ProjectId,
    string Title,
    string Summary,
    DateTimeOffset LastUpdatedAt,
    int MessageCount,
    string TranscriptPath,
    ThreadWorktreeMetadataDto Worktree);

public sealed record ThreadMessageDto(
    string Id,
    string ThreadId,
    string Role,
    string Content,
    DateTimeOffset Timestamp);

public sealed record ThreadDetailDto(
    ThreadSummaryDto Thread,
    IReadOnlyList<ThreadMessageDto> Messages);
