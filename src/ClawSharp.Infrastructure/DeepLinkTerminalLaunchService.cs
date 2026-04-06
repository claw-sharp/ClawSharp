
namespace ClawSharp.Infrastructure;

public sealed record DeepLinkHandleResult(
    bool Success,
    string? Error = null);

public sealed class DeepLinkTerminalLaunchService
{
    private readonly DeepLinkProtocolService _protocolService;
    private readonly GitHubRepoPathMappingService _repoPathMappingService;
    private readonly TerminalLauncher _terminalLauncher;

    public DeepLinkTerminalLaunchService(
        DeepLinkProtocolService? protocolService = null,
        GitHubRepoPathMappingService? repoPathMappingService = null,
        TerminalLauncher? terminalLauncher = null)
    {
        _protocolService = protocolService ?? new DeepLinkProtocolService();
        _repoPathMappingService = repoPathMappingService ?? new GitHubRepoPathMappingService();
        _terminalLauncher = terminalLauncher ?? new TerminalLauncher();
    }

    public async Task<DeepLinkHandleResult> HandleUriAsync(
        string uri,
        string executablePath,
        CancellationToken cancellationToken = default)
    {
        DeepLinkAction action;
        try
        {
            action = _protocolService.Parse(uri);
        }
        catch (Exception exception)
        {
            return new DeepLinkHandleResult(false, $"Deep link error: {exception.Message}");
        }

        var (cwd, resolvedRepo) = await ResolveCwdAsync(action, cancellationToken).ConfigureAwait(false);
        var launched = await LaunchInTerminalAsync(
            executablePath,
            cwd,
            BuildReplArguments(action.Query, resolvedRepo),
            cancellationToken).ConfigureAwait(false);
        return launched
            ? new DeepLinkHandleResult(true)
            : new DeepLinkHandleResult(
                false,
                "Failed to open a terminal. Make sure a supported terminal emulator is installed.");
    }

    private async Task<(string Cwd, string? ResolvedRepo)> ResolveCwdAsync(
        DeepLinkAction action,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(action.Cwd))
        {
            return (action.Cwd!, null);
        }

        if (!string.IsNullOrWhiteSpace(action.Repo))
        {
            var resolved = await _repoPathMappingService.ResolveRepoAsync(action.Repo!, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return (resolved!, action.Repo);
            }
        }

        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return (string.IsNullOrWhiteSpace(homeDirectory) ? Directory.GetCurrentDirectory() : homeDirectory, null);
    }

    private static IReadOnlyList<string> BuildReplArguments(string? draftPrompt, string? resolvedRepo)
    {
        var arguments = new List<string>
        {
            "repl",
            "--deep-link-origin"
        };

        if (!string.IsNullOrWhiteSpace(resolvedRepo))
        {
            arguments.Add("--deep-link-repo");
            arguments.Add(resolvedRepo);
        }

        if (!string.IsNullOrWhiteSpace(draftPrompt))
        {
            arguments.Add("--deep-link-draft");
            arguments.Add(draftPrompt);
        }

        return arguments;
    }

    private async Task<bool> LaunchInTerminalAsync(
        string executablePath,
        string cwd,
        IReadOnlyList<string> replArguments,
        CancellationToken cancellationToken)
    {
        return await _terminalLauncher.LaunchAsync(executablePath, replArguments, cwd, cancellationToken).ConfigureAwait(false);
    }
}
