namespace ClawSharp.Core;

public sealed class ClawSharpSettings
{
    public RuntimeSettings Runtime { get; init; } = new();
    public TerminalSettings Terminal { get; init; } = new();
    public SandboxSettings Sandbox { get; init; } = new();
    public string? ClaudeApiKey { get; init; }
    public bool? SkipAutoPermissionPrompt { get; init; }
    public bool? UseAutoModeDuringPlan { get; init; }
    public string? ApiKeyHelper { get; init; }
    public string? AwsCredentialExport { get; init; }
    public string? AwsAuthRefresh { get; init; }
    public string? Agent { get; init; }
    public AttributionSettings? Attribution { get; init; }
    public PermissionSettings Permissions { get; init; } = new();
    public bool AllowManagedPermissionRulesOnly { get; init; }
    public IReadOnlyDictionary<HookEvent, IReadOnlyList<HookMatcherDefinition>> Hooks { get; init; } =
        new Dictionary<HookEvent, IReadOnlyList<HookMatcherDefinition>>();
    public bool DisableAllHooks { get; init; }
    public bool AllowManagedHooksOnly { get; init; }
    public string? ForceLoginOrgUUID { get; init; }
    public string? OtelHeadersHelper { get; init; }
    public IReadOnlyDictionary<string, PluginEnabledSetting> EnabledPlugins { get; init; } =
        new Dictionary<string, PluginEnabledSetting>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, PluginConfigSettings> PluginConfigs { get; init; } =
        new Dictionary<string, PluginConfigSettings>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, AgentModelConnection> AgentModels { get; init; } =
        new Dictionary<string, AgentModelConnection>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, string> AgentRouting { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
