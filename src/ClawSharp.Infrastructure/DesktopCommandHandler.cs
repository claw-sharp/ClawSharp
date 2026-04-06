using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class DesktopCommandHandler : ICommandHandler
{
    private readonly DesktopDeepLinkService _desktopDeepLinkService;

    public DesktopCommandHandler(DesktopDeepLinkService desktopDeepLinkService)
    {
        _desktopDeepLinkService = desktopDeepLinkService;
    }

    public CommandDescriptor Descriptor { get; } =
        new(
            "desktop",
            "Continue the current session in Claude Desktop",
            "/desktop [download]",
            IsInteractive: true,
            Aliases: ["app"],
            Availability: [CommandAvailability.ClaudeAi],
            IsEnabled: DesktopDeepLinkService.IsSupportedPlatform());

    public async Task<CommandResult> ExecuteAsync(
        string input,
        CommandExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var argument = ParseArgument(input);
        if (string.Equals(argument, "download", StringComparison.OrdinalIgnoreCase))
        {
            var url = _desktopDeepLinkService.GetDownloadUrl();
            var opened = await BrowserLauncher.OpenBrowserAsync(url, cancellationToken: cancellationToken).ConfigureAwait(false);
            return new CommandResult(
                true,
                opened
                    ? $"Opened download URL: {url}"
                    : $"Download Claude Desktop from {url}");
        }

        var status = await _desktopDeepLinkService.GetInstallStatusAsync(cancellationToken).ConfigureAwait(false);
        if (string.Equals(status.Status, "not-installed", StringComparison.Ordinal))
        {
            return new CommandResult(
                true,
                "Claude Desktop is not installed. Run /desktop download or visit https://claude.ai/download");
        }

        if (string.Equals(status.Status, "version-too-old", StringComparison.Ordinal))
        {
            return new CommandResult(
                true,
                $"Claude Desktop needs to be updated (found v{status.Version}, need v{DesktopDeepLinkService.MinimumDesktopVersion}+). Run /desktop download.");
        }

        var openResult = await _desktopDeepLinkService.OpenCurrentSessionInDesktopAsync(
            context.Session,
            cancellationToken).ConfigureAwait(false);
        return new CommandResult(
            true,
            openResult.Success
                ? $"Session transferred to Claude Desktop ({openResult.DeepLinkUrl})"
                : openResult.Error ?? "Failed to open Claude Desktop.");
    }

    private static string? ParseArgument(string input)
    {
        var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 1 ? parts[1] : null;
    }
}
