// TS origin: ./utils/telemetry/pluginTelemetry.ts, ./utils/telemetry/skillLoadedEvent.ts
using System.Security.Cryptography;
using System.Text;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

internal static class PluginTelemetryUtilities
{
    private const string PluginIdHashSalt = "claude-plugin-telemetry-v1";

    public static void LogPluginsEnabledForSession(IReadOnlyList<DiscoveredPlugin> plugins)
    {
        foreach (var plugin in plugins.Where(static plugin => plugin.Enabled))
        {
            var (name, marketplace) = ParsePluginIdentifier(plugin.PluginId);
            ClawSharpTelemetry.LogEvent(
                "tengu_plugin_enabled_for_session",
                new Dictionary<string, object?>
                {
                    ["plugin_id_hash"] = HashPluginId(name, marketplace),
                    ["plugin_scope"] = GetPluginScope(plugin, marketplace),
                    ["plugin_name_redacted"] = IsAnthropicControlled(plugin, marketplace) ? name : "third-party",
                    ["marketplace_name_redacted"] = IsAnthropicControlled(plugin, marketplace) && marketplace is not null ? marketplace : "third-party",
                    ["is_official_plugin"] = IsAnthropicControlled(plugin, marketplace),
                    ["is_builtin"] = plugin.IsBundled,
                    ["skill_path_count"] = plugin.Manifest?.Skills.Count ?? 0,
                    ["command_path_count"] = plugin.Manifest?.Commands.Count ?? 0,
                    ["has_hooks"] = plugin.Hooks.Count > 0,
                    ["validation_issue_count"] = plugin.ValidationIssues.Count,
                    ["version"] = plugin.Manifest?.Version
                });
            ClawSharpTelemetry.RecordMetric(
                "plugin.enabled.count",
                1,
                new Dictionary<string, object?>
                {
                    ["plugin_scope"] = GetPluginScope(plugin, marketplace)
                });
        }
    }

    public static void LogPluginLoadFailures(IReadOnlyList<DiscoveredPlugin> plugins)
    {
        foreach (var plugin in plugins)
        {
            var (name, marketplace) = ParsePluginIdentifier(plugin.PluginId);
            foreach (var issue in plugin.ValidationIssues)
            {
                ClawSharpTelemetry.LogEvent(
                    "tengu_plugin_load_failed",
                    new Dictionary<string, object?>
                    {
                        ["plugin_id_hash"] = HashPluginId(name, marketplace),
                        ["plugin_scope"] = GetPluginScope(plugin, marketplace),
                        ["error_category"] = issue.IsWarning ? "warning" : "validation",
                        ["issue_path"] = issue.Path
                    });
                ClawSharpTelemetry.RecordMetric(
                    "plugin.load_failed.count",
                    1,
                    new Dictionary<string, object?>
                    {
                        ["severity"] = issue.IsWarning ? "warning" : "error"
                    });
            }
        }
    }

    public static void LogSkillsLoaded(IReadOnlyList<DiscoveredSkill> skills)
    {
        foreach (var skill in skills)
        {
            ClawSharpTelemetry.LogEvent(
                "tengu_skill_loaded",
                new Dictionary<string, object?>
                {
                    ["skill_source"] = skill.Source,
                    ["skill_loaded_from"] = skill.BaseDirectory,
                    ["skill_name"] = skill.Name
                });
        }

        ClawSharpTelemetry.RecordMetric("skill.loaded.count", skills.Count);
    }

    private static string HashPluginId(string name, string? marketplace)
    {
        var key = marketplace is null ? name : $"{name}@{marketplace.ToLowerInvariant()}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key + PluginIdHashSalt));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static (string Name, string? Marketplace) ParsePluginIdentifier(string pluginId)
    {
        var separator = pluginId.LastIndexOf('@');
        return separator < 0
            ? (pluginId, null)
            : (pluginId[..separator], pluginId[(separator + 1)..]);
    }

    private static bool IsAnthropicControlled(DiscoveredPlugin plugin, string? marketplace)
    {
        return plugin.IsBundled || string.Equals(marketplace, "builtin", StringComparison.Ordinal);
    }

    private static string GetPluginScope(DiscoveredPlugin plugin, string? marketplace)
    {
        if (plugin.IsBundled || string.Equals(marketplace, "builtin", StringComparison.Ordinal))
        {
            return "default-bundle";
        }

        return "user-local";
    }
}
