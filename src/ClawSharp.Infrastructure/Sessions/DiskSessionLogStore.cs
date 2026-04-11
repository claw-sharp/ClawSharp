using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class DiskSessionLogStore : ISessionLogStore
{
    private readonly SessionLogMetadataStore _sessionLogMetadataStore;

    public DiskSessionLogStore(SessionLogMetadataStore? sessionLogMetadataStore = null)
    {
        _sessionLogMetadataStore = sessionLogMetadataStore ?? new SessionLogMetadataStore();
    }

    public async Task<IReadOnlyList<SessionLog>> LoadProjectLogsAsync(
        string projectDirectory,
        CancellationToken cancellationToken = default)
    {
        return await _sessionLogMetadataStore.LoadProjectLogsAsync(projectDirectory, cancellationToken);
    }

    public async Task<IReadOnlyList<SessionLog>> LoadSameRepoLogsAsync(
        IReadOnlyList<string> worktreePaths,
        CancellationToken cancellationToken = default)
    {
        if (worktreePaths.Count == 0)
        {
            return [];
        }

        if (worktreePaths.Count == 1)
        {
            return await LoadProjectLogsAsync(worktreePaths[0], cancellationToken);
        }

        var projectsDir = SessionStoragePaths.GetProjectsDir();
        if (!Directory.Exists(projectsDir))
        {
            return await LoadProjectLogsAsync(worktreePaths[0], cancellationToken);
        }

        var worktreePrefixes = worktreePaths
            .Select(
                path => new WorktreePrefix(
                    path,
                    NormalizeDirectoryName(SessionStoragePaths.SanitizePath(path))))
            .OrderByDescending(prefix => prefix.SanitizedPrefix.Length)
            .ToArray();

        var logs = new List<SessionLog>();
        var seenDirectories = new HashSet<string>(GetPathComparer());
        foreach (var directory in Directory.GetDirectories(projectsDir))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var directoryName = NormalizeDirectoryName(Path.GetFileName(directory));
            if (seenDirectories.Contains(directoryName))
            {
                continue;
            }

            foreach (var worktreePrefix in worktreePrefixes)
            {
                if (!MatchesWorktreeDirectory(directoryName, worktreePrefix.SanitizedPrefix))
                {
                    continue;
                }

                seenDirectories.Add(directoryName);
                logs.AddRange(
                    await LoadProjectLogsAsync(
                        worktreePrefix.WorktreePath,
                        cancellationToken));
                break;
            }
        }

        return DeduplicateBySessionId(logs);
    }

    public async Task<IReadOnlyList<SessionLog>> SearchProjectLogsByCustomTitleAsync(
        string projectDirectory,
        string title,
        bool exact = true,
        CancellationToken cancellationToken = default)
    {
        var normalizedTitle = title.Trim();
        if (normalizedTitle.Length == 0)
        {
            return [];
        }

        var logs = await LoadProjectLogsAsync(projectDirectory, cancellationToken);
        return logs
            .Where(
                log =>
                    !string.IsNullOrWhiteSpace(log.CustomTitle) &&
                    TitleMatches(log.CustomTitle!, normalizedTitle, exact))
            .ToArray();
    }

    public async Task<IReadOnlyList<SessionLog>> SearchSameRepoLogsByCustomTitleAsync(
        IReadOnlyList<string> worktreePaths,
        string title,
        bool exact = true,
        CancellationToken cancellationToken = default)
    {
        var normalizedTitle = title.Trim();
        if (normalizedTitle.Length == 0)
        {
            return [];
        }

        var logs = await LoadSameRepoLogsAsync(worktreePaths, cancellationToken);
        return logs
            .Where(
                log =>
                    !string.IsNullOrWhiteSpace(log.CustomTitle) &&
                    TitleMatches(log.CustomTitle!, normalizedTitle, exact))
            .ToArray();
    }

    public async Task<SessionLog?> FindSameRepoLogBySessionIdAsync(
        IReadOnlyList<string> worktreePaths,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParseExact(sessionId, "N", out _))
        {
            return null;
        }

        var logs = await LoadSameRepoLogsAsync(worktreePaths, cancellationToken);
        return logs
            .Where(log => string.Equals(log.SessionId, sessionId, StringComparison.Ordinal))
            .OrderByDescending(log => log.Modified)
            .FirstOrDefault();
    }

    private static bool TitleMatches(string candidate, string title, bool exact)
    {
        return exact
            ? string.Equals(candidate.Trim(), title, StringComparison.OrdinalIgnoreCase)
            : candidate.Contains(title, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<SessionLog> DeduplicateBySessionId(IReadOnlyList<SessionLog> logs)
    {
        var deduplicated = new Dictionary<string, SessionLog>(StringComparer.Ordinal);
        foreach (var log in logs.OrderByDescending(log => log.Modified))
        {
            if (!deduplicated.ContainsKey(log.SessionId))
            {
                deduplicated[log.SessionId] = log;
            }
        }

        return deduplicated.Values
            .OrderByDescending(log => log.Modified)
            .ToArray();
    }

    private static bool MatchesWorktreeDirectory(string directoryName, string sanitizedPrefix)
    {
        return directoryName == sanitizedPrefix || directoryName.StartsWith(sanitizedPrefix + "-", GetComparison());
    }

    private static string NormalizeDirectoryName(string value)
    {
        return OperatingSystem.IsWindows() ? value.ToLowerInvariant() : value;
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    private static StringComparison GetComparison()
    {
        return OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    private sealed record WorktreePrefix(string WorktreePath, string SanitizedPrefix);
}
