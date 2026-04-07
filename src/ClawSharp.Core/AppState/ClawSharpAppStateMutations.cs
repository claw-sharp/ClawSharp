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
                CloneRuntimeSettings(
                    state.Settings.Runtime,
                    permissionMode: transitionedContext.Mode))
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
                CloneRuntimeSettings(
                    state.Settings.Runtime,
                    model: nextModel))
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
                CloneRuntimeSettings(state.Settings.Runtime),
                normalizedApiKey)
        };
    }

    public static ClawSharpAppState WithSettings(
        ClawSharpAppState state,
        ClawSharpSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return state with
        {
            Settings = settings,
            MainLoopModel = MainLoopModelResolver.Resolve(settings.Runtime.Model)
        };
    }

    private static RuntimeSettings CloneRuntimeSettings(
        RuntimeSettings source,
        PermissionMode? permissionMode = null,
        string? model = null)
    {
        return new RuntimeSettings
        {
            PermissionMode = permissionMode ?? source.PermissionMode,
            Model = model ?? source.Model,
            FallbackModel = source.FallbackModel,
            EnableTelemetry = source.EnableTelemetry,
            FileCheckpointingEnabled = source.FileCheckpointingEnabled,
            AutoMemoryEnabled = source.AutoMemoryEnabled,
            AutoMemoryDirectory = source.AutoMemoryDirectory
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
            Sandbox = new SandboxSettings
            {
                Enabled = source.Sandbox.Enabled,
                AllowUnsandboxedCommands = source.Sandbox.AllowUnsandboxedCommands,
                FailIfUnavailable = source.Sandbox.FailIfUnavailable
            },
            ClaudeApiKey = claudeApiKeyOverride ?? source.ClaudeApiKey,
            SkipAutoPermissionPrompt = source.SkipAutoPermissionPrompt,
            UseAutoModeDuringPlan = source.UseAutoModeDuringPlan,
            ApiKeyHelper = source.ApiKeyHelper,
            AwsCredentialExport = source.AwsCredentialExport,
            AwsAuthRefresh = source.AwsAuthRefresh,
            Agent = source.Agent,
            Attribution = source.Attribution,
            Permissions = source.Permissions,
            AllowManagedPermissionRulesOnly = source.AllowManagedPermissionRulesOnly,
            Hooks = source.Hooks,
            DisableAllHooks = source.DisableAllHooks,
            AllowManagedHooksOnly = source.AllowManagedHooksOnly,
            ForceLoginOrgUUID = source.ForceLoginOrgUUID,
            OtelHeadersHelper = source.OtelHeadersHelper,
            EnabledPlugins = source.EnabledPlugins,
            PluginConfigs = source.PluginConfigs,
            AgentModels = source.AgentModels,
            AgentRouting = source.AgentRouting
        };
    }
}
