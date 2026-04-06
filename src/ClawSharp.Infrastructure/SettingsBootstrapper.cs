// TS origin: ./utils/settings/settings.ts, ./utils/settings/validation.ts, ./utils/envUtils.ts
using System.Text.Json;
using System.Text.Json.Serialization;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class SettingsBootstrapper
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            new PluginEnabledSettingJsonConverter(),
            new HookCommandDefinitionJsonConverter()
        }
    };

    public async Task<SettingsLoadResult> LoadAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        StartupProfiler.Checkpoint("loadSettingsFromDisk_start");
        var merged = new PartialClawSharpSettings();
        var issues = new List<SettingsLoadIssue>();
        var sourcePreferences = new Dictionary<PermissionRuleSource, SettingsSourcePreferences>();
        var configHome = SessionStoragePaths.GetClaudeConfigHomeDir();

        foreach (var (source, sourcePath) in GetOrderedSettingsPaths(workspaceRoot, configHome))
        {
            var partial = await TryLoadPartialAsync(sourcePath, issues, cancellationToken);
            if (partial is null)
            {
                continue;
            }

            merged = Merge(merged, partial);
            sourcePreferences[source] = new SettingsSourcePreferences(
                partial.SkipAutoPermissionPrompt,
                partial.UseAutoModeDuringPlan);
        }

        var result = new SettingsLoadResult(ToSettings(merged), issues, sourcePreferences);
        StartupProfiler.Checkpoint("loadSettingsFromDisk_end");
        return result;
    }

    private static IReadOnlyList<(PermissionRuleSource Source, string Path)> GetOrderedSettingsPaths(string workspaceRoot, string configHome)
    {
        return
        [
            (PermissionRuleSource.UserSettings, Path.Combine(configHome, "settings.json")),
            (PermissionRuleSource.ProjectSettings, ClaudeConfigPaths.GetProjectSettingsFilePath(workspaceRoot)),
            (PermissionRuleSource.LocalSettings, ClaudeConfigPaths.GetLocalSettingsFilePath(workspaceRoot)),
            (PermissionRuleSource.PolicySettings, ClaudeConfigPaths.GetManagedSettingsFilePath())
        ];
    }

    private static async Task<PartialClawSharpSettings?> TryLoadPartialAsync(
        string filePath,
        ICollection<SettingsLoadIssue> issues,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(filePath);
            var partial = await JsonSerializer.DeserializeAsync<PartialClawSharpSettings>(
                stream,
                SerializerOptions,
                cancellationToken);

            return partial ?? new PartialClawSharpSettings();
        }
        catch (JsonException exception)
        {
            issues.Add(new SettingsLoadIssue(
                filePath,
                exception.Path ?? string.Empty,
                exception.Message));
            return null;
        }
    }

    private static PartialClawSharpSettings Merge(PartialClawSharpSettings current, PartialClawSharpSettings next)
    {
        return new PartialClawSharpSettings
        {
            Runtime = Merge(current.Runtime, next.Runtime),
            Terminal = Merge(current.Terminal, next.Terminal),
            Sandbox = Merge(current.Sandbox, next.Sandbox),
            ClaudeApiKey = next.ClaudeApiKey ?? current.ClaudeApiKey,
            SkipAutoPermissionPrompt = next.SkipAutoPermissionPrompt ?? current.SkipAutoPermissionPrompt,
            UseAutoModeDuringPlan = next.UseAutoModeDuringPlan ?? current.UseAutoModeDuringPlan,
            ApiKeyHelper = next.ApiKeyHelper ?? current.ApiKeyHelper,
            AwsCredentialExport = next.AwsCredentialExport ?? current.AwsCredentialExport,
            AwsAuthRefresh = next.AwsAuthRefresh ?? current.AwsAuthRefresh,
            Permissions = Merge(current.Permissions, next.Permissions),
            AllowManagedPermissionRulesOnly = next.AllowManagedPermissionRulesOnly ?? current.AllowManagedPermissionRulesOnly,
            Hooks = MergeHooks(current.Hooks, next.Hooks),
            DisableAllHooks = next.DisableAllHooks ?? current.DisableAllHooks,
            AllowManagedHooksOnly = next.AllowManagedHooksOnly ?? current.AllowManagedHooksOnly,
            ForceLoginOrgUUID = next.ForceLoginOrgUUID ?? current.ForceLoginOrgUUID,
            OtelHeadersHelper = next.OtelHeadersHelper ?? current.OtelHeadersHelper,
            EnabledPlugins = MergePluginEnablement(current.EnabledPlugins, next.EnabledPlugins),
            PluginConfigs = MergePluginConfigs(current.PluginConfigs, next.PluginConfigs),
            AgentModels = MergeAgentModels(current.AgentModels, next.AgentModels),
            AgentRouting = MergeAgentRouting(current.AgentRouting, next.AgentRouting)
        };
    }

    private static PartialRuntimeSettings? Merge(PartialRuntimeSettings? current, PartialRuntimeSettings? next)
    {
        if (current is null)
        {
            return next;
        }

        if (next is null)
        {
            return current;
        }

        return new PartialRuntimeSettings
        {
            PermissionMode = next.PermissionMode ?? current.PermissionMode,
            Model = next.Model ?? current.Model,
            EnableTelemetry = next.EnableTelemetry ?? current.EnableTelemetry,
            FileCheckpointingEnabled = next.FileCheckpointingEnabled ?? current.FileCheckpointingEnabled,
            AutoMemoryEnabled = next.AutoMemoryEnabled ?? current.AutoMemoryEnabled,
            AutoMemoryDirectory = next.AutoMemoryDirectory ?? current.AutoMemoryDirectory
        };
    }

    private static PartialTerminalSettings? Merge(PartialTerminalSettings? current, PartialTerminalSettings? next)
    {
        if (current is null)
        {
            return next;
        }

        if (next is null)
        {
            return current;
        }

        return new PartialTerminalSettings
        {
            ShowTimestamps = next.ShowTimestamps ?? current.ShowTimestamps,
            UseColor = next.UseColor ?? current.UseColor
        };
    }

    private static PartialSandboxSettings? Merge(PartialSandboxSettings? current, PartialSandboxSettings? next)
    {
        if (current is null)
        {
            return next;
        }

        if (next is null)
        {
            return current;
        }

        return new PartialSandboxSettings
        {
            Enabled = next.Enabled ?? current.Enabled,
            AllowUnsandboxedCommands = next.AllowUnsandboxedCommands ?? current.AllowUnsandboxedCommands,
            FailIfUnavailable = next.FailIfUnavailable ?? current.FailIfUnavailable
        };
    }

    private static ClawSharpSettings ToSettings(PartialClawSharpSettings partial)
    {
        var defaults = new ClawSharpSettings();

        return new ClawSharpSettings
        {
            Runtime = new RuntimeSettings
            {
                PermissionMode = partial.Runtime?.PermissionMode ?? defaults.Runtime.PermissionMode,
                Model = partial.Runtime?.Model ?? defaults.Runtime.Model,
                EnableTelemetry = partial.Runtime?.EnableTelemetry ?? defaults.Runtime.EnableTelemetry,
                FileCheckpointingEnabled = partial.Runtime?.FileCheckpointingEnabled ?? defaults.Runtime.FileCheckpointingEnabled,
                AutoMemoryEnabled = partial.Runtime?.AutoMemoryEnabled ?? defaults.Runtime.AutoMemoryEnabled,
                AutoMemoryDirectory = partial.Runtime?.AutoMemoryDirectory ?? defaults.Runtime.AutoMemoryDirectory
            },
            Terminal = new TerminalSettings
            {
                ShowTimestamps = partial.Terminal?.ShowTimestamps ?? defaults.Terminal.ShowTimestamps,
                UseColor = partial.Terminal?.UseColor ?? defaults.Terminal.UseColor
            },
            Sandbox = new SandboxSettings
            {
                Enabled = partial.Sandbox?.Enabled ?? defaults.Sandbox.Enabled,
                AllowUnsandboxedCommands = partial.Sandbox?.AllowUnsandboxedCommands ?? defaults.Sandbox.AllowUnsandboxedCommands,
                FailIfUnavailable = partial.Sandbox?.FailIfUnavailable ?? defaults.Sandbox.FailIfUnavailable
            },
            ClaudeApiKey = partial.ClaudeApiKey ?? defaults.ClaudeApiKey,
            SkipAutoPermissionPrompt = partial.SkipAutoPermissionPrompt ?? defaults.SkipAutoPermissionPrompt,
            UseAutoModeDuringPlan = partial.UseAutoModeDuringPlan ?? defaults.UseAutoModeDuringPlan,
            ApiKeyHelper = partial.ApiKeyHelper ?? defaults.ApiKeyHelper,
            AwsCredentialExport = partial.AwsCredentialExport ?? defaults.AwsCredentialExport,
            AwsAuthRefresh = partial.AwsAuthRefresh ?? defaults.AwsAuthRefresh,
            Permissions = new PermissionSettings
            {
                Allow = partial.Permissions?.Allow ?? defaults.Permissions.Allow,
                Deny = partial.Permissions?.Deny ?? defaults.Permissions.Deny,
                Ask = partial.Permissions?.Ask ?? defaults.Permissions.Ask,
                DefaultMode = partial.Permissions?.DefaultMode ?? defaults.Permissions.DefaultMode,
                DisableBypassPermissionsMode = partial.Permissions?.DisableBypassPermissionsMode ?? defaults.Permissions.DisableBypassPermissionsMode,
                DisableAutoMode = partial.Permissions?.DisableAutoMode ?? defaults.Permissions.DisableAutoMode,
                AdditionalDirectories = partial.Permissions?.AdditionalDirectories ?? defaults.Permissions.AdditionalDirectories
            },
            AllowManagedPermissionRulesOnly = partial.AllowManagedPermissionRulesOnly ?? defaults.AllowManagedPermissionRulesOnly,
            Hooks = partial.Hooks ?? defaults.Hooks,
            DisableAllHooks = partial.DisableAllHooks ?? defaults.DisableAllHooks,
            AllowManagedHooksOnly = partial.AllowManagedHooksOnly ?? defaults.AllowManagedHooksOnly,
            ForceLoginOrgUUID = partial.ForceLoginOrgUUID ?? defaults.ForceLoginOrgUUID,
            OtelHeadersHelper = partial.OtelHeadersHelper ?? defaults.OtelHeadersHelper,
            EnabledPlugins = partial.EnabledPlugins ?? defaults.EnabledPlugins,
            PluginConfigs = partial.PluginConfigs ?? defaults.PluginConfigs,
            AgentModels = partial.AgentModels ?? defaults.AgentModels,
            AgentRouting = partial.AgentRouting ?? defaults.AgentRouting
        };
    }

    private static PartialPermissionSettings? Merge(PartialPermissionSettings? current, PartialPermissionSettings? next)
    {
        if (current is null)
        {
            return next;
        }

        if (next is null)
        {
            return current;
        }

        return new PartialPermissionSettings
        {
            Allow = next.Allow ?? current.Allow,
            Deny = next.Deny ?? current.Deny,
            Ask = next.Ask ?? current.Ask,
            DefaultMode = next.DefaultMode ?? current.DefaultMode,
            DisableBypassPermissionsMode = next.DisableBypassPermissionsMode ?? current.DisableBypassPermissionsMode,
            DisableAutoMode = next.DisableAutoMode ?? current.DisableAutoMode,
            AdditionalDirectories = next.AdditionalDirectories ?? current.AdditionalDirectories
        };
    }

    private static IReadOnlyDictionary<HookEvent, IReadOnlyList<HookMatcherDefinition>>? MergeHooks(
        IReadOnlyDictionary<HookEvent, IReadOnlyList<HookMatcherDefinition>>? current,
        IReadOnlyDictionary<HookEvent, IReadOnlyList<HookMatcherDefinition>>? next)
    {
        if (current is null)
        {
            return next;
        }

        if (next is null)
        {
            return current;
        }

        var merged = new Dictionary<HookEvent, IReadOnlyList<HookMatcherDefinition>>(current);
        foreach (var pair in next)
        {
            var combined = new List<HookMatcherDefinition>();
            if (merged.TryGetValue(pair.Key, out var existing))
            {
                combined.AddRange(existing);
            }

            combined.AddRange(pair.Value);
            merged[pair.Key] = combined;
        }

        return merged;
    }

    private static IReadOnlyDictionary<string, PluginEnabledSetting>? MergePluginEnablement(
        IReadOnlyDictionary<string, PluginEnabledSetting>? current,
        IReadOnlyDictionary<string, PluginEnabledSetting>? next)
    {
        if (current is null)
        {
            return next;
        }

        if (next is null)
        {
            return current;
        }

        var merged = new Dictionary<string, PluginEnabledSetting>(current, StringComparer.Ordinal);
        foreach (var pair in next)
        {
            merged[pair.Key] = pair.Value;
        }

        return merged;
    }

    private static IReadOnlyDictionary<string, PluginConfigSettings>? MergePluginConfigs(
        IReadOnlyDictionary<string, PluginConfigSettings>? current,
        IReadOnlyDictionary<string, PluginConfigSettings>? next)
    {
        if (current is null)
        {
            return next;
        }

        if (next is null)
        {
            return current;
        }

        var merged = new Dictionary<string, PluginConfigSettings>(current, StringComparer.Ordinal);
        foreach (var pair in next)
        {
            if (!merged.TryGetValue(pair.Key, out var existing))
            {
                merged[pair.Key] = pair.Value;
                continue;
            }

            var options = new Dictionary<string, object?>(existing.Options, StringComparer.Ordinal);
            foreach (var option in pair.Value.Options)
            {
                options[option.Key] = option.Value;
            }

            merged[pair.Key] = new PluginConfigSettings { Options = options };
        }

        return merged;
    }

    private static IReadOnlyDictionary<string, AgentModelConnection>? MergeAgentModels(
        IReadOnlyDictionary<string, AgentModelConnection>? current,
        IReadOnlyDictionary<string, AgentModelConnection>? next)
    {
        if (current is null)
        {
            return next;
        }

        if (next is null)
        {
            return current;
        }

        var merged = new Dictionary<string, AgentModelConnection>(current, StringComparer.Ordinal);
        foreach (var pair in next)
        {
            merged[pair.Key] = pair.Value;
        }

        return merged;
    }

    private static IReadOnlyDictionary<string, string>? MergeAgentRouting(
        IReadOnlyDictionary<string, string>? current,
        IReadOnlyDictionary<string, string>? next)
    {
        if (current is null)
        {
            return next;
        }

        if (next is null)
        {
            return current;
        }

        var merged = new Dictionary<string, string>(current, StringComparer.Ordinal);
        foreach (var pair in next)
        {
            merged[pair.Key] = pair.Value;
        }

        return merged;
    }

    private sealed class PartialClawSharpSettings
    {
        public PartialRuntimeSettings? Runtime { get; init; }
        public PartialTerminalSettings? Terminal { get; init; }
        public PartialSandboxSettings? Sandbox { get; init; }
        public string? ClaudeApiKey { get; init; }
        public bool? SkipAutoPermissionPrompt { get; init; }
        public bool? UseAutoModeDuringPlan { get; init; }
        public string? ApiKeyHelper { get; init; }
        public string? AwsCredentialExport { get; init; }
        public string? AwsAuthRefresh { get; init; }
        public PartialPermissionSettings? Permissions { get; init; }
        public bool? AllowManagedPermissionRulesOnly { get; init; }
        public IReadOnlyDictionary<HookEvent, IReadOnlyList<HookMatcherDefinition>>? Hooks { get; init; }
        public bool? DisableAllHooks { get; init; }
        public bool? AllowManagedHooksOnly { get; init; }
        public string? ForceLoginOrgUUID { get; init; }
        public string? OtelHeadersHelper { get; init; }
        public IReadOnlyDictionary<string, PluginEnabledSetting>? EnabledPlugins { get; init; }
        public IReadOnlyDictionary<string, PluginConfigSettings>? PluginConfigs { get; init; }
        public IReadOnlyDictionary<string, AgentModelConnection>? AgentModels { get; init; }
        public IReadOnlyDictionary<string, string>? AgentRouting { get; init; }
    }

    private sealed class PartialRuntimeSettings
    {
        public PermissionMode? PermissionMode { get; init; }
        public string? Model { get; init; }
        public bool? EnableTelemetry { get; init; }
        public bool? FileCheckpointingEnabled { get; init; }
        public bool? AutoMemoryEnabled { get; init; }
        public string? AutoMemoryDirectory { get; init; }
    }

    private sealed class PartialTerminalSettings
    {
        public bool? ShowTimestamps { get; init; }
        public bool? UseColor { get; init; }
    }

    private sealed class PartialSandboxSettings
    {
        public bool? Enabled { get; init; }
        public bool? AllowUnsandboxedCommands { get; init; }
        public bool? FailIfUnavailable { get; init; }
    }

    private sealed class PartialPermissionSettings
    {
        public IReadOnlyList<string>? Allow { get; init; }
        public IReadOnlyList<string>? Deny { get; init; }
        public IReadOnlyList<string>? Ask { get; init; }
        public PermissionMode? DefaultMode { get; init; }
        public string? DisableBypassPermissionsMode { get; init; }
        public string? DisableAutoMode { get; init; }
        public IReadOnlyList<string>? AdditionalDirectories { get; init; }
    }
}
