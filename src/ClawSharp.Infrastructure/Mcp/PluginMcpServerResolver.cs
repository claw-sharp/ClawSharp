using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class PluginMcpServerResolver
{
    private readonly PluginOptionService _pluginOptionService;

    public PluginMcpServerResolver(
        IMcpSecureStorage secureStorage,
        string? pluginsDirectoryPath = null)
    {
        _pluginOptionService = new PluginOptionService(secureStorage, pluginsDirectoryPath);
    }

    public (IReadOnlyDictionary<string, ScopedMcpServerConfig> Servers, IReadOnlyList<McpConfigError> Errors) Resolve(
        IReadOnlyList<DiscoveredPlugin> plugins,
        ClawSharpSettings settings)
    {
        var servers = new Dictionary<string, ScopedMcpServerConfig>(StringComparer.Ordinal);
        var errors = new List<McpConfigError>();

        foreach (var plugin in plugins
                     .Where(static plugin => plugin.Enabled && plugin.Manifest?.McpServers.Count > 0)
                     .OrderBy(static plugin => plugin.PluginId, StringComparer.Ordinal))
        {
            var options = _pluginOptionService.LoadPluginOptions(plugin.PluginId, settings);
            foreach (var server in plugin.Manifest!.McpServers)
            {
                try
                {
                    var resolved = McpServerConfigStringTransformer.Transform(
                        server.Config,
                        value => _pluginOptionService.SubstituteUserConfigVariables(value, options));

                    servers[server.Name] = new ScopedMcpServerConfig(
                        server.Name,
                        resolved,
                        McpConfigScope.Dynamic,
                        PluginSource: plugin.PluginId);
                }
                catch (InvalidOperationException exception)
                {
                    errors.Add(new McpConfigError(
                        plugin.InstallPath,
                        $"mcpServers.{server.Name}",
                        exception.Message,
                        "Provide the required plugin option values to enable this MCP server.",
                        new McpConfigErrorMetadata(McpConfigScope.Dynamic, server.Name, McpConfigErrorSeverity.Warning)));
                }
            }
        }

        return (servers, errors);
    }
}
