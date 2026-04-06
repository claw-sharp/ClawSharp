// TS origin: ./commands/resume/index.ts, ./commands/resume/resume.tsx
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class ResumeCommandHandler : ICommandHandler
{
    private readonly ISessionLogStore _sessionLogStore;
    private readonly IWorktreePathResolver _worktreePathResolver;

    public ResumeCommandHandler(
        ISessionLogStore sessionLogStore,
        IWorktreePathResolver worktreePathResolver)
    {
        _sessionLogStore = sessionLogStore;
        _worktreePathResolver = worktreePathResolver;
    }

    public CommandDescriptor Descriptor { get; } =
        new(
            "resume",
            "Resume a previous conversation",
            "/resume [conversation id]",
            IsInteractive: true,
            Aliases: ["continue"]);

    public async Task<CommandResult> ExecuteAsync(
        string input,
        CommandExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var argument = ParseArgument(input);
        if (string.IsNullOrWhiteSpace(argument))
        {
            if (context.AppStateStore.GetState().Environment.IsNonInteractive)
            {
                return new CommandResult(true, "Usage: /resume <session-id> (interactive picker is disabled in headless mode)");
            }

            var worktreePaths = await _worktreePathResolver.GetWorktreePathsAsync(
                context.Session.ProjectDirectory,
                cancellationToken);
            if (worktreePaths.Count <= 1)
            {
                worktreePaths = [context.Session.ProjectDirectory];
            }

            var recentLogs = await _sessionLogStore.LoadSameRepoLogsAsync(worktreePaths, cancellationToken);
            if (recentLogs.Count == 0)
            {
                return new CommandResult(true, "No recent sessions found to resume.");
            }

            var options = recentLogs.Select(l => $"{l.SessionId.PadRight(32)} {l.CustomTitle ?? "(Untitled session)"}").ToList();
            var selected = await context.InteractionService.SelectAsync("Select a session to resume:", options, cancellationToken);
            if (string.IsNullOrWhiteSpace(selected))
            {
                return new CommandResult(true, "No session selected.");
            }

            argument = selected.Split(' ')[0];
        }

        var (resumedSession, error) = await ResolveSessionAsync(context, argument, cancellationToken);
        if (resumedSession is null)
        {
            return new CommandResult(true, error ?? $"Session {argument} was not found.");
        }

        return new CommandResult(
            true,
            $"Resumed session {resumedSession.Id}.",
            resumedSession);
    }

    private static string? ParseArgument(string input)
    {
        var trimmed = input.Trim();
        var firstSpace = trimmed.IndexOf(' ');
        if (firstSpace < 0 || firstSpace == trimmed.Length - 1)
        {
            return null;
        }

        return trimmed[(firstSpace + 1)..].Trim();
    }

    private async Task<(ConversationSession? Session, string? Error)> ResolveSessionAsync(
        CommandExecutionContext context,
        string argument,
        CancellationToken cancellationToken)
    {
        var worktreePaths = await _worktreePathResolver.GetWorktreePathsAsync(
            context.Session.ProjectDirectory,
            cancellationToken);
        if (worktreePaths.Count <= 1)
        {
            worktreePaths = [context.Session.ProjectDirectory];
        }

        if (Guid.TryParseExact(argument, "N", out _))
        {
            var resumedById = await context.SessionFactory.ResumeAsync(argument, cancellationToken);
            if (resumedById is not null)
            {
                return (resumedById, null);
            }

            var matchingLog = await _sessionLogStore.FindSameRepoLogBySessionIdAsync(
                worktreePaths,
                argument,
                cancellationToken);
            return matchingLog is null
                ? (null, null)
                : (await context.SessionFactory.ResumeAsync(matchingLog, cancellationToken), null);
        }

        var titleMatches = await _sessionLogStore.SearchSameRepoLogsByCustomTitleAsync(
            worktreePaths,
            argument,
            exact: true,
            cancellationToken);
        if (titleMatches.Count == 1)
        {
            return (await context.SessionFactory.ResumeAsync(titleMatches[0], cancellationToken), null);
        }

        if (titleMatches.Count > 1)
        {
            return (null, $"Found {titleMatches.Count} sessions matching {argument}. Please use /resume to pick a specific session.");
        }

        return (null, null);
    }
}
