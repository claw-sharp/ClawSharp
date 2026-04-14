using System.Text;
using System.Text.RegularExpressions;
using ClawSharp.Core;

namespace ClawSharp.Query.Plugins;

internal sealed class PromptPluginReferenceService
{
    private static readonly Regex PluginReferencePattern = new(
        @"(?<![\w:])\$(?<name>[A-Za-z0-9][A-Za-z0-9:_-]*)",
        RegexOptions.Compiled);
    private static readonly Regex LeadingPluginReferencePattern = new(
        @"^\s*\$(?<name>[A-Za-z0-9][A-Za-z0-9:_-]*)\s+(?<rest>.+?)\s*$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    public Task<QueryTurnRequest> ExpandPromptPluginReferencesAsync(
        QueryTurnRequest request,
        IReadOnlyList<DiscoveredPlugin> discoveredPlugins,
        CancellationToken cancellationToken = default)
    {
        if (discoveredPlugins.Count == 0 || string.IsNullOrWhiteSpace(request.EffectiveUserInput))
        {
            return Task.FromResult(request);
        }

        var referencedPluginNames = PluginReferencePattern.Matches(request.EffectiveUserInput)
            .Select(static match => match.Groups["name"].Value)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (referencedPluginNames.Length == 0)
        {
            return Task.FromResult(request);
        }

        var modelTurnContext = request.ModelTurnContext ?? QueryModelTurnContext.ReplMainThread;
        var userContext = new Dictionary<string, string>(modelTurnContext.UserContext, StringComparer.Ordinal);
        var addedPluginCount = 0;

        foreach (var referencedPluginName in referencedPluginNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var plugin = ResolvePlugin(discoveredPlugins, referencedPluginName);
            if (plugin?.Manifest is null)
            {
                continue;
            }

            userContext[$"Plugin ${GetReferenceName(plugin)}"] = BuildInjectedPrompt(plugin);
            addedPluginCount += 1;
        }

        if (addedPluginCount == 0)
        {
            return Task.FromResult(request);
        }

        return Task.FromResult(request with
        {
            ResolvedUserInput = ResolveUserInput(request.EffectiveUserInput, discoveredPlugins),
            ModelTurnContext = new QueryModelTurnContext(
                modelTurnContext.SystemPrompt,
                userContext,
                modelTurnContext.SystemContext,
                modelTurnContext.QuerySource)
        });
    }

    private static DiscoveredPlugin? ResolvePlugin(
        IReadOnlyList<DiscoveredPlugin> plugins,
        string requestedPluginName)
    {
        var matches = plugins
            .Where(static plugin => plugin.Enabled)
            .Where(plugin => MatchesReference(plugin, requestedPluginName))
            .Take(2)
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }

    private static bool MatchesReference(DiscoveredPlugin plugin, string requestedPluginName)
    {
        return string.Equals(GetReferenceName(plugin), requestedPluginName, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(plugin.Name, requestedPluginName, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveUserInput(string effectiveUserInput, IReadOnlyList<DiscoveredPlugin> plugins)
    {
        var match = LeadingPluginReferencePattern.Match(effectiveUserInput);
        if (!match.Success)
        {
            return null;
        }

        var plugin = ResolvePlugin(plugins, match.Groups["name"].Value);
        if (plugin is null)
        {
            return null;
        }

        var rest = match.Groups["rest"].Value.Trim();
        return rest.Length == 0 ? null : rest;
    }

    private static string BuildInjectedPrompt(DiscoveredPlugin plugin)
    {
        var manifest = plugin.Manifest!;
        var builder = new StringBuilder();
        builder.Append("Plugin $");
        builder.Append(GetReferenceName(plugin));
        builder.AppendLine(" has been explicitly selected by the user.");
        builder.AppendLine("Prefer this plugin's capabilities when they fit the task.");
        builder.Append("Plugin id: ");
        builder.AppendLine(plugin.PluginId);
        if (!string.IsNullOrWhiteSpace(manifest.Description))
        {
            builder.Append("Description: ");
            builder.AppendLine(manifest.Description.Trim());
        }

        if (manifest.McpServers.Count > 0)
        {
            builder.AppendLine("Bundled MCP servers:");
            foreach (var server in manifest.McpServers)
            {
                builder.Append("- ");
                builder.Append(server.Name);
                builder.Append(" (");
                builder.Append(server.Config.Type);
                builder.AppendLine(")");
                builder.Append("  Prefer MCP tools with prefix ");
                builder.Append(BuildMcpToolPrefix(server.Name));
                builder.AppendLine(" when relevant.");
            }
        }

        if (manifest.Skills.Count > 0)
        {
            builder.AppendLine("Bundled skills:");
            foreach (var skill in manifest.Skills)
            {
                builder.Append("- $");
                builder.AppendLine(skill);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildMcpToolPrefix(string serverName)
    {
        var builder = new StringBuilder(serverName.Length);
        foreach (var character in serverName)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_');
        }

        return $"mcp__{builder}__";
    }

    private static string GetReferenceName(DiscoveredPlugin plugin)
    {
        var preferred = plugin.PluginId.Split('@', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                        ?? plugin.Name;
        var builder = new StringBuilder(preferred.Length);
        foreach (var character in preferred.Trim().ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) || character is ':' or '_' or '-' ? character : '-');
        }

        return builder.ToString().Trim('-');
    }
}
