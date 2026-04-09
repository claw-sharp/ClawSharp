namespace ClawSharp.Core.Worktree;

public sealed class NullWorktreeService : IWorktreeService
{
    public Task<WorktreeSession> CreateWorktreeForSessionAsync(string sessionId, string slug, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Worktree isolation is not implemented in this runner.");
    }

    public Task ExitWorktreeAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public WorktreeSession? GetCurrentWorktreeSession()
    {
        return null;
    }
}
