using System.Text.RegularExpressions;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class PluginOptionService
{
    private static readonly Regex PluginRootPattern = new(@"\$\{CLAUDE_PLUGIN_ROOT\}", RegexOptions.Compiled);
    private static readonly Regex PluginDataPattern = new(@"\$\{CLAUDE_PLUGIN_DATA\}", RegexOptions.Compiled);
    private static readonly Regex UserConfigPattern = new(@"\$\{user_config\.([^}]+)\}", RegexOptions.Compiled);

    private readonly IMcpSecureStorage _secureStorage;
    private readonly string _pluginsDirectoryPath;

    public PluginOptionService(
        IMcpSecureStorage secureStorage,
        string? pluginsDirectoryPath = null)
    {
        _secureStorage = secureStorage;
        _pluginsDirectoryPath = pluginsDirectoryPath ?? ClaudeConfigPaths.GetPluginsDirectoryPath();
    }

    public IReadOnlyDictionary<string, object?> LoadPluginOptions(
        string pluginId,
        ClawSharpSettings settings)
    {
        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (settings.PluginConfigs.TryGetValue(pluginId, out var config))
        {
            foreach (var option in config.Options)
            {
                merged[option.Key] = option.Value;
            }
        }

        var sensitive = _secureStorage.Read()?.PluginSecrets?.GetValueOrDefault(pluginId);
        if (sensitive is not null)
        {
            foreach (var option in sensitive)
            {
                merged[option.Key] = option.Value;
            }
        }

        return merged;
    }

    public void SavePluginOptions(
        string pluginId,
        IReadOnlyDictionary<string, object?> values,
        IReadOnlyDictionary<string, PluginOptionDefinition> schema,
        ClawSharpSettings settings,
        out ClawSharpSettings updatedSettings)
    {
        var existingNonSensitive = settings.PluginConfigs.TryGetValue(pluginId, out var existingConfig)
            ? new Dictionary<string, object?>(existingConfig.Options, StringComparer.Ordinal)
            : new Dictionary<string, object?>(StringComparer.Ordinal);
        var existingSensitive = _secureStorage.Read()?.PluginSecrets?.GetValueOrDefault(pluginId) is { } existingSecrets
            ? new Dictionary<string, string>(existingSecrets, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        var nonSensitive = new Dictionary<string, object?>(existingNonSensitive, StringComparer.Ordinal);
        var sensitive = new Dictionary<string, string>(existingSensitive, StringComparer.Ordinal);

        foreach (var pair in values)
        {
            if (schema.TryGetValue(pair.Key, out var definition) && definition.Sensitive)
            {
                if (pair.Value is null)
                {
                    sensitive.Remove(pair.Key);
                }
                else
                {
                    sensitive[pair.Key] = Convert.ToString(pair.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                }
            }
            else
            {
                if (pair.Value is null)
                {
                    nonSensitive.Remove(pair.Key);
                }
                else
                {
                    nonSensitive[pair.Key] = pair.Value;
                }
            }
        }

        var secureData = _secureStorage.Read() ?? new McpSecureStorageData();
        var pluginSecrets = secureData.PluginSecrets is null
            ? new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            : new Dictionary<string, IReadOnlyDictionary<string, string>>(secureData.PluginSecrets, StringComparer.Ordinal);
        if (sensitive.Count > 0)
        {
            pluginSecrets[pluginId] = sensitive;
        }
        else
        {
            pluginSecrets.Remove(pluginId);
        }

        _secureStorage.Update(secureData with { PluginSecrets = pluginSecrets.Count == 0 ? null : pluginSecrets });

        var configs = new Dictionary<string, PluginConfigSettings>(settings.PluginConfigs, StringComparer.Ordinal)
        {
            [pluginId] = new PluginConfigSettings { Options = nonSensitive }
        };

        updatedSettings = CloneSettings(settings, configs);
    }

    public ClawSharpSettings DeletePluginOptions(string pluginId, ClawSharpSettings settings)
    {
        var configs = new Dictionary<string, PluginConfigSettings>(settings.PluginConfigs, StringComparer.Ordinal);
        configs.Remove(pluginId);

        var secureData = _secureStorage.Read();
        if (secureData?.PluginSecrets is not null && secureData.PluginSecrets.ContainsKey(pluginId))
        {
            var pluginSecrets = new Dictionary<string, IReadOnlyDictionary<string, string>>(secureData.PluginSecrets, StringComparer.Ordinal);
            pluginSecrets.Remove(pluginId);
            _secureStorage.Update(secureData with { PluginSecrets = pluginSecrets.Count == 0 ? null : pluginSecrets });
        }

        return CloneSettings(settings, configs);
    }

    public string SubstitutePluginVariables(string value, string pluginRoot, string? pluginId)
    {
        var normalizedRoot = NormalizePath(pluginRoot);
        var substituted = PluginRootPattern.Replace(value, normalizedRoot);
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return substituted;
        }

        var dataDir = EnsurePluginDataDirectory(pluginId);
        return PluginDataPattern.Replace(substituted, NormalizePath(dataDir));
    }

    public string SubstituteUserConfigVariables(
        string value,
        IReadOnlyDictionary<string, object?> options)
    {
        return UserConfigPattern.Replace(value, match =>
        {
            var key = match.Groups[1].Value;
            if (!options.TryGetValue(key, out var resolved) || resolved is null)
            {
                throw new InvalidOperationException(
                    $"Missing required user configuration value: {key}. This should have been validated before variable substitution.");
            }

            return Convert.ToString(resolved, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        });
    }

    public string SubstituteUserConfigInContent(
        string value,
        IReadOnlyDictionary<string, object?> options,
        IReadOnlyDictionary<string, PluginOptionDefinition> schema)
    {
        return UserConfigPattern.Replace(value, match =>
        {
            var key = match.Groups[1].Value;
            if (schema.TryGetValue(key, out var definition) && definition.Sensitive)
            {
                return $"[sensitive option '{key}' not available in skill content]";
            }

            return options.TryGetValue(key, out var resolved) && resolved is not null
                ? Convert.ToString(resolved, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
                : match.Value;
        });
    }

    public string GetPluginDataDirectory(string pluginId)
    {
        return EnsurePluginDataDirectory(pluginId);
    }

    private string EnsurePluginDataDirectory(string pluginId)
    {
        var sanitized = SanitizePluginSegment(pluginId);
        var path = Path.Combine(_pluginsDirectoryPath, "data", sanitized);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string NormalizePath(string path)
    {
        return PathUtilities.NormalizePathForConfigKey(path);
    }

    private static string SanitizePluginSegment(string value)
    {
        var buffer = new char[value.Length];
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            buffer[index] =
                (character >= 'a' && character <= 'z') ||
                (character >= 'A' && character <= 'Z') ||
                (character >= '0' && character <= '9') ||
                character is '-' or '_'
                    ? character
                    : '-';
        }

        return new string(buffer);
    }

    private static ClawSharpSettings CloneSettings(
        ClawSharpSettings settings,
        IReadOnlyDictionary<string, PluginConfigSettings> pluginConfigs)
    {
        return new ClawSharpSettings
        {
            Runtime = settings.Runtime,
            Terminal = settings.Terminal,
            Hooks = settings.Hooks,
            DisableAllHooks = settings.DisableAllHooks,
            AllowManagedHooksOnly = settings.AllowManagedHooksOnly,
            EnabledPlugins = settings.EnabledPlugins,
            PluginConfigs = pluginConfigs
        };
    }
}
