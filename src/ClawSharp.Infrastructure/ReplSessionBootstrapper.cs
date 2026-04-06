// TS origin: ./main.tsx, ./commands/resume/resume.tsx, ./utils/sessionStorage.ts
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class ReplSessionBootstrapper
{
    private readonly ISessionFactory _sessionFactory;
    private readonly ISessionLogStore _sessionLogStore;
    private readonly ITranscriptStore _transcriptStore;
    private readonly IWorktreePathResolver _worktreePathResolver;
    private readonly string _workspaceRoot;

    public ReplSessionBootstrapper(
        ISessionFactory sessionFactory,
        ISessionLogStore sessionLogStore,
        ITranscriptStore transcriptStore,
        IWorktreePathResolver worktreePathResolver,
        string workspaceRoot)
    {
        _sessionFactory = sessionFactory;
        _sessionLogStore = sessionLogStore;
        _transcriptStore = transcriptStore;
        _worktreePathResolver = worktreePathResolver;
        _workspaceRoot = workspaceRoot;
    }

    public async Task<(bool Success, ConversationSession? Session, string? Error)> ResolveAsync(
        IReadOnlyList<string> replArgs,
        CancellationToken cancellationToken = default)
    {
        var continueMostRecent = false;
        string? resumeArgument = null;
        string? sessionName = null;

        for (var index = 0; index < replArgs.Count; index++)
        {
            switch (replArgs[index])
            {
                case "--debug":
                case "-d":
                case "--debug-to-stderr":
                case "-d2e":
                    break;
                case "--continue":
                case "-c":
                    if (continueMostRecent || resumeArgument is not null)
                    {
                        return (false, null, "Only one of --continue or --resume may be specified.");
                    }

                    continueMostRecent = true;
                    break;
                case "--resume":
                case "-r":
                    if (continueMostRecent || resumeArgument is not null)
                    {
                        return (false, null, "Only one of --continue or --resume may be specified.");
                    }

                    if (index + 1 >= replArgs.Count)
                    {
                        return (false, null, "Interactive resume picker is not implemented yet. Use --resume <session-id>.");
                    }

                    resumeArgument = replArgs[++index];
                    break;
                case "--name":
                case "-n":
                    if (index + 1 >= replArgs.Count)
                    {
                        return (false, null, "Missing value for --name.");
                    }

                    sessionName = replArgs[++index].Trim();
                    if (sessionName.Length == 0)
                    {
                        return (false, null, "Missing value for --name.");
                    }

                    break;
                case "--debug-file":
                    if (index + 1 >= replArgs.Count)
                    {
                        return (false, null, "Missing value for --debug-file.");
                    }

                    index++;
                    break;
                default:
                    if (replArgs[index].StartsWith("--debug=", StringComparison.Ordinal) ||
                        replArgs[index].StartsWith("--debug-file=", StringComparison.Ordinal))
                    {
                        break;
                    }

                    return (false, null, $"Unknown repl argument: {replArgs[index]}");
            }
        }

        ConversationSession? session = null;
        if (continueMostRecent)
        {
            session = await _sessionFactory.ContinueMostRecentAsync(cancellationToken);
            if (session is null)
            {
                return (false, null, "No conversations found to continue.");
            }
        }
        else if (resumeArgument is not null)
        {
            var (resolvedSession, error) = await ResolveResumeArgumentAsync(resumeArgument, cancellationToken);
            session = resolvedSession;
            if (session is null)
            {
                return (false, null, error ?? $"Session {resumeArgument} was not found.");
            }
        }

        if (sessionName is not null)
        {
            session ??= _sessionFactory.Create();
            session.SetCustomTitle(sessionName);
            await _transcriptStore.RecordSessionMetadataAsync(session, cancellationToken);
        }

        return (true, session, null);
    }

    private async Task<(ConversationSession? Session, string? Error)> ResolveResumeArgumentAsync(
        string argument,
        CancellationToken cancellationToken)
    {
        var worktreePaths = await _worktreePathResolver.GetWorktreePathsAsync(_workspaceRoot, cancellationToken);
        if (worktreePaths.Count <= 1)
        {
            worktreePaths = [_workspaceRoot];
        }

        if (Guid.TryParseExact(argument, "N", out _))
        {
            var resumedById = await _sessionFactory.ResumeAsync(argument, cancellationToken);
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
                : (await _sessionFactory.ResumeAsync(matchingLog, cancellationToken), null);
        }

        var titleMatches = await _sessionLogStore.SearchSameRepoLogsByCustomTitleAsync(
            worktreePaths,
            argument,
            exact: true,
            cancellationToken);
        if (titleMatches.Count == 1)
        {
            return (await _sessionFactory.ResumeAsync(titleMatches[0], cancellationToken), null);
        }

        if (titleMatches.Count > 1)
        {
            return (null, $"Found {titleMatches.Count} sessions matching {argument}. Please use /resume to pick a specific session.");
        }

        return (null, null);
    }
}
