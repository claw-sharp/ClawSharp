using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class ChromeCommandHandler : ICommandHandler
{
    private const string ChromeExtensionUrl = "https://claude.ai/chrome";
    private const string ChromePermissionsUrl = "https://clau.de/chrome/permissions";
    private const string ChromeReconnectUrl = "https://clau.de/chrome/reconnect";
    private const string ChromeDocsUrl = "https://code.clawsharp.com/docs/en/chrome";

    public CommandDescriptor Descriptor { get; } =
        new(
            "chrome",
            "Claude in Chrome (Beta) settings",
            "/chrome [install|permissions|reconnect|docs]",
            IsInteractive: true,
            Availability: [CommandAvailability.ClaudeAi]);

    public async Task<CommandResult> ExecuteAsync(
        string input,
        CommandExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var command = ParseArgument(input);
        var url = command switch
        {
            "install" => ChromeExtensionUrl,
            "permissions" => ChromePermissionsUrl,
            "reconnect" => ChromeReconnectUrl,
            "docs" => ChromeDocsUrl,
            _ => null
        };

        if (url is not null)
        {
            var opened = await BrowserLauncher.OpenBrowserAsync(url, cancellationToken: cancellationToken).ConfigureAwait(false);
            return new CommandResult(
                true,
                opened
                    ? $"Opened {url}"
                    : $"Failed to open {url}");
        }

        return new CommandResult(
            true,
            string.Join(
                Environment.NewLine,
                [
                    "Claude in Chrome (Beta)",
                    $"Install extension: {ChromeExtensionUrl}",
                    $"Manage permissions: {ChromePermissionsUrl}",
                    $"Reconnect extension: {ChromeReconnectUrl}",
                    $"Learn more: {ChromeDocsUrl}"
                ]));
    }

    private static string? ParseArgument(string input)
    {
        var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 1 ? parts[1] : null;
    }
}
