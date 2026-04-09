using ClawSharp.Tasks;

namespace ClawSharp.Core;

public sealed record ClawSharpAppState(
    string WorkspaceRoot,
    StartupEnvironment Environment,
    ClawSharpSettings Settings,
    ToolPermissionContext ToolPermissionContext,
    IReadOnlyList<SettingsLoadIssue> SettingsIssues,
    IReadOnlyList<DiscoveredPluginInstallation> PluginInstallations,
    IReadOnlyList<DiscoveredPlugin> Plugins,
    IReadOnlyList<DiscoveredSkill> Skills,
    IReadOnlyList<AgentDefinition> AgentDefinitions,
    IReadOnlyList<HookDefinition> Hooks,
    IReadOnlyDictionary<string, string> AgentNameRegistry,
    IReadOnlyDictionary<string, ClawSharpTask> Tasks,
    bool Verbose = false,
    string? MainLoopModel = null,
    string? StatusLineText = null,
    string? ActiveSessionId = null,
    string? ActiveSessionProjectDirectory = null,
    string? ActiveSessionTitle = null,
    string? ForegroundedTaskId = null,
    string? ViewingAgentTaskId = null,
    PendingPlanVerification? PendingPlanVerification = null)
{
    public static ClawSharpAppState CreateDefault(
        string workspaceRoot,
        StartupEnvironment environment,
        ClawSharpSettings settings,
        IReadOnlyList<SettingsLoadIssue> settingsIssues,
        IReadOnlyList<DiscoveredPluginInstallation> pluginInstallations,
        IReadOnlyList<DiscoveredPlugin> plugins,
        IReadOnlyList<DiscoveredSkill> skills,
        IReadOnlyList<AgentDefinition> agentDefinitions,
        IReadOnlyList<HookDefinition> hooks,
        ToolPermissionContext? toolPermissionContext = null)
    {
        return new ClawSharpAppState(
            workspaceRoot,
            environment,
            settings,
            toolPermissionContext ?? ToolPermissionContexts.CreateEmpty(settings.Runtime.PermissionMode),
            settingsIssues,
            pluginInstallations,
            plugins,
            skills,
            agentDefinitions,
            hooks,
            new Dictionary<string, string>(StringComparer.Ordinal),
            new Dictionary<string, ClawSharpTask>(StringComparer.Ordinal),
            MainLoopModel: MainLoopModelResolver.Resolve(settings.Runtime.Model));
    }
}
