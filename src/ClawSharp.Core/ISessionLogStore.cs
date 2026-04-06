namespace ClawSharp.Core;

public interface ISessionLogStore
{
    Task<IReadOnlyList<SessionLog>> LoadProjectLogsAsync(
        string projectDirectory,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionLog>> LoadSameRepoLogsAsync(
        IReadOnlyList<string> worktreePaths,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionLog>> SearchProjectLogsByCustomTitleAsync(
        string projectDirectory,
        string title,
        bool exact = true,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionLog>> SearchSameRepoLogsByCustomTitleAsync(
        IReadOnlyList<string> worktreePaths,
        string title,
        bool exact = true,
        CancellationToken cancellationToken = default);

    Task<SessionLog?> FindSameRepoLogBySessionIdAsync(
        IReadOnlyList<string> worktreePaths,
        string sessionId,
        CancellationToken cancellationToken = default);
}
