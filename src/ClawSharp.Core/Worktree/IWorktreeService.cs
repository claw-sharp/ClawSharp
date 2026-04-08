namespace ClawSharp.Core.Worktree;

public record WorktreeSession(
    string OriginalCwd,
    string WorktreePath,
    string WorktreeName,
    string? WorktreeBranch = null,
    string? OriginalBranch = null,
    string? OriginalHeadCommit = null,
    string? SessionId = null);

public interface IWorktreeService
{
    Task<WorktreeSession> CreateWorktreeForSessionAsync(string sessionId, string slug, CancellationToken cancellationToken = default);
    Task ExitWorktreeAsync(CancellationToken cancellationToken = default);
    WorktreeSession? GetCurrentWorktreeSession();
}
