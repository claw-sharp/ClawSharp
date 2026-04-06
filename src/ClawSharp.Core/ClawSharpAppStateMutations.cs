// TS origin: ./state/AppStateStore.ts, ./state/onChangeAppState.ts
using ClawSharp.Tasks;

namespace ClawSharp.Core;

public static class ClawSharpAppStateMutations
{
    public static ClawSharpAppState WithTasks(
        ClawSharpAppState state,
        IReadOnlyDictionary<string, ClawSharpTask> tasks)
    {
        return state with
        {
            Tasks = new Dictionary<string, ClawSharpTask>(tasks, StringComparer.Ordinal)
        };
    }

    public static ClawSharpAppState WithAgentNameRegistration(
        ClawSharpAppState state,
        string agentName,
        string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentName) || string.IsNullOrWhiteSpace(agentId))
        {
            return state;
        }

        var nextRegistry = new Dictionary<string, string>(state.AgentNameRegistry, StringComparer.Ordinal)
        {
            [agentName] = agentId
        };

        return state with { AgentNameRegistry = nextRegistry };
    }

    public static ClawSharpAppState WithoutAgentNameRegistration(
        ClawSharpAppState state,
        string agentName)
    {
        if (string.IsNullOrWhiteSpace(agentName) || !state.AgentNameRegistry.ContainsKey(agentName))
        {
            return state;
        }

        var nextRegistry = new Dictionary<string, string>(state.AgentNameRegistry, StringComparer.Ordinal);
        nextRegistry.Remove(agentName);
        return state with { AgentNameRegistry = nextRegistry };
    }

    public static ClawSharpAppState WithActiveSession(
        ClawSharpAppState state,
        ConversationSession? session)
    {
        return state with
        {
            ActiveSessionId = session?.Id,
            ActiveSessionProjectDirectory = session?.ProjectDirectory,
            ActiveSessionTitle = session?.CustomTitle
        };
    }

    public static ClawSharpAppState WithStatusLineText(
        ClawSharpAppState state,
        string? statusLineText)
    {
        return state with { StatusLineText = statusLineText };
    }

    public static ClawSharpAppState WithForegroundedTaskId(
        ClawSharpAppState state,
        string? foregroundedTaskId)
    {
        return state with { ForegroundedTaskId = string.IsNullOrWhiteSpace(foregroundedTaskId) ? null : foregroundedTaskId };
    }

    public static ClawSharpAppState WithViewingAgentTaskId(
        ClawSharpAppState state,
        string? viewingAgentTaskId)
    {
        return state with { ViewingAgentTaskId = string.IsNullOrWhiteSpace(viewingAgentTaskId) ? null : viewingAgentTaskId };
    }

    public static ClawSharpAppState WithToolPermissionMode(
        ClawSharpAppState state,
        PermissionMode mode,
        bool useAutoModeDuringPlan = false)
    {
        var effectiveUseAutoModeDuringPlan = useAutoModeDuringPlan ||
                                             state.ToolPermissionContext.UseAutoModeDuringPlan;
        var transitionedContext = PermissionModeTransition.Transition(
            state.ToolPermissionContext,
            mode,
            effectiveUseAutoModeDuringPlan);

        return state with
        {
            ToolPermissionContext = transitionedContext,
            Settings = CloneSettings(
                state.Settings,
                new RuntimeSettings
                {
                    PermissionMode = transitionedContext.Mode,
                    Model = state.Settings.Runtime.Model,
                    EnableTelemetry = state.Settings.Runtime.EnableTelemetry,
                    FileCheckpointingEnabled = state.Settings.Runtime.FileCheckpointingEnabled
                })
        };
    }

    public static ClawSharpAppState WithMainLoopModel(
        ClawSharpAppState state,
        string? model)
    {
        var nextModel = MainLoopModelResolver.Resolve(model, state.Settings.Runtime.Model);

        return state with
        {
            MainLoopModel = nextModel,
            Settings = CloneSettings(
                state.Settings,
                new RuntimeSettings
                {
                    PermissionMode = state.Settings.Runtime.PermissionMode,
                    Model = nextModel,
                    EnableTelemetry = state.Settings.Runtime.EnableTelemetry,
                    FileCheckpointingEnabled = state.Settings.Runtime.FileCheckpointingEnabled
                })
        };
    }

    public static ClawSharpAppState WithClaudeApiKey(
        ClawSharpAppState state,
        string? apiKey)
    {
        var normalizedApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();

        return state with
        {
            Settings = CloneSettings(
                state.Settings,
                new RuntimeSettings
                {
                    PermissionMode = state.Settings.Runtime.PermissionMode,
                    Model = state.Settings.Runtime.Model,
                    EnableTelemetry = state.Settings.Runtime.EnableTelemetry,
                    FileCheckpointingEnabled = state.Settings.Runtime.FileCheckpointingEnabled
                },
                normalizedApiKey)
        };
    }

    private static ClawSharpSettings CloneSettings(
        ClawSharpSettings source,
        RuntimeSettings runtime,
        string? claudeApiKeyOverride = null)
    {
        return new ClawSharpSettings
        {
            Runtime = runtime,
            Terminal = new TerminalSettings
            {
                ShowTimestamps = source.Terminal.ShowTimestamps,
                UseColor = source.Terminal.UseColor
            },
            ClaudeApiKey = claudeApiKeyOverride ?? source.ClaudeApiKey,
            ApiKeyHelper = source.ApiKeyHelper,
            AwsCredentialExport = source.AwsCredentialExport,
            AwsAuthRefresh = source.AwsAuthRefresh,
            Agent = source.Agent,
            Permissions = source.Permissions,
            AllowManagedPermissionRulesOnly = source.AllowManagedPermissionRulesOnly,
            Hooks = source.Hooks,
            DisableAllHooks = source.DisableAllHooks,
            AllowManagedHooksOnly = source.AllowManagedHooksOnly,
            ForceLoginOrgUUID = source.ForceLoginOrgUUID,
            OtelHeadersHelper = source.OtelHeadersHelper,
            EnabledPlugins = source.EnabledPlugins,
            PluginConfigs = source.PluginConfigs
        };
    }
}
