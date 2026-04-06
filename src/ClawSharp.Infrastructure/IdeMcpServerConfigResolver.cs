using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class IdeMcpServerConfigResolver
{
    private readonly IdeIntegrationService _ideIntegrationService;

    public IdeMcpServerConfigResolver(IdeIntegrationService ideIntegrationService)
    {
        _ideIntegrationService = ideIntegrationService;
    }

    public async Task<ScopedMcpServerConfig?> TryResolveAsync(CancellationToken cancellationToken = default)
    {
        var ide = (await _ideIntegrationService.DetectIdesAsync(includeInvalid: false, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault();
        if (ide is null)
        {
            return null;
        }

        if (ide.Url.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
            ide.Url.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
        {
            return new ScopedMcpServerConfig(
                "ide",
                new McpWebSocketIdeServerConfig(
                    ide.Url,
                    ide.Name,
                    ide.AuthToken,
                    ide.RunningInWindows),
                McpConfigScope.Dynamic);
        }

        if (ide.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            ide.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return new ScopedMcpServerConfig(
                "ide",
                new McpSseIdeServerConfig(
                    ide.Url,
                    ide.Name,
                    ide.RunningInWindows),
                McpConfigScope.Dynamic);
        }

        return null;
    }
}
