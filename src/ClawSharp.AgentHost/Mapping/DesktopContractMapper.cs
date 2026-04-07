using System.Security.Cryptography;
using System.Text;
using ClawSharp.AgentHost.Contracts;
using ClawSharp.Core;

namespace ClawSharp.AgentHost.Mapping;

public static class DesktopContractMapper
{
    public static ProjectSummaryDto MapProject(
        string projectPath,
        IReadOnlyList<ThreadSummaryDto> threads,
        DateTimeOffset? lastOpenedAt = null)
    {
        var normalizedPath = Path.GetFullPath(projectPath);
        return new ProjectSummaryDto(
            Id: CreateProjectId(normalizedPath),
            Name: GetProjectName(normalizedPath),
            Path: normalizedPath,
            LastOpenedAt: lastOpenedAt ?? DateTimeOffset.UtcNow,
            LastUpdatedAt: threads.FirstOrDefault()?.LastUpdatedAt,
            ThreadCount: threads.Count,
            GitBranch: null);
    }

    public static ThreadSummaryDto MapThreadSummary(string projectId, SessionLog log)
    {
        return new ThreadSummaryDto(
            Id: log.SessionId,
            ProjectId: projectId,
            Title: GetThreadTitle(log.CustomTitle, log.FirstUserMessage, log.SessionId),
            Summary: GetThreadSummary(log.FirstUserMessage),
            LastUpdatedAt: log.Modified,
            MessageCount: 0,
            TranscriptPath: log.TranscriptPath,
            Worktree: new ThreadWorktreeMetadataDto(
                RepoRoot: log.ProjectDirectory,
                WorktreePath: log.ProjectDirectory,
                WorktreeStatus: null,
                BaseBranch: null,
                BaseCommit: null));
    }

    public static ThreadDetailDto MapThreadDetail(
        string projectId,
        ConversationSession session,
        DateTimeOffset lastUpdatedAt)
    {
        var messages = session.Messages
            .Select(
                message => new ThreadMessageDto(
                    message.Id,
                    session.Id,
                    message.Role.ToString().ToLowerInvariant(),
                    message.Content,
                    message.Timestamp))
            .ToArray();

        var thread = new ThreadSummaryDto(
            Id: session.Id,
            ProjectId: projectId,
            Title: GetThreadTitle(session.CustomTitle, messages.FirstOrDefault(static message => message.Role == "user")?.Content, session.Id),
            Summary: GetThreadSummary(messages.FirstOrDefault(static message => message.Role == "user")?.Content),
            LastUpdatedAt: lastUpdatedAt,
            MessageCount: messages.Length,
            TranscriptPath: session.TranscriptPath,
            Worktree: new ThreadWorktreeMetadataDto(
                RepoRoot: session.ProjectDirectory,
                WorktreePath: session.ProjectDirectory,
                WorktreeStatus: null,
                BaseBranch: null,
                BaseCommit: null));

        return new ThreadDetailDto(thread, messages);
    }

    public static string CreateProjectId(string projectPath)
    {
        var normalizedPath = Path.GetFullPath(projectPath).Normalize(NormalizationForm.FormC);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        return Convert.ToHexStringLower(hash[..8]);
    }

    private static string GetProjectName(string projectPath)
    {
        var name = Path.GetFileName(projectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? projectPath : name;
    }

    private static string GetThreadTitle(string? customTitle, string? firstUserMessage, string sessionId)
    {
        if (!string.IsNullOrWhiteSpace(customTitle))
        {
            return customTitle.Trim();
        }

        if (!string.IsNullOrWhiteSpace(firstUserMessage))
        {
            var trimmed = firstUserMessage.Trim();
            return trimmed.Length <= 80 ? trimmed : trimmed[..80];
        }

        return $"Thread {sessionId[..Math.Min(8, sessionId.Length)]}";
    }

    private static string GetThreadSummary(string? firstUserMessage)
    {
        if (string.IsNullOrWhiteSpace(firstUserMessage))
        {
            return "No messages yet";
        }

        var trimmed = firstUserMessage.Trim();
        return trimmed.Length <= 160 ? trimmed : trimmed[..160];
    }
}
