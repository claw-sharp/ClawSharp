using ClawSharp.Core;

namespace ClawSharp.Ui.Terminal;

public sealed class SettingsCommandRenderer
{
    public string Render(CommandExecutionContext context)
    {
        var state = context.AppStateStore.GetState();
        var settings = context.Settings;
        var session = context.Session;
        var providerConfig = ProviderRuntimeResolver.Resolve(settings, state.MainLoopModel ?? settings.Runtime.Model);
        var lines = new List<string>
        {
            "Settings",
            string.Empty,
            "Status",
            $"Version: {AppMetadata.DisplayVersion}",
            $"Session name: {GetSessionName(session)}",
            $"Session ID: {session.Id}",
            $"cwd: {session.ProjectDirectory}",
            $"Workspace root: {state.WorkspaceRoot}",
            $"Provider: {providerConfig.Provider}",
            $"Provider endpoint: {providerConfig.BaseUrl}",
            $"Model: {FormatModel(state.MainLoopModel ?? settings.Runtime.Model)}",
            $"Active permission mode: {state.ToolPermissionContext.Mode}",
            $"Background tasks: {state.Tasks.Count}",
            $"Plugin installations: {state.PluginInstallations.Count}",
            $"Plugins discovered: {state.Plugins.Count}",
            $"Skills discovered: {state.Skills.Count}",
            $"Agents available: {state.AgentDefinitions.Count}",
            $"Hooks loaded: {state.Hooks.Count}",
            $"Bare mode: {FormatBoolValue(state.Environment.BareMode)}",
            $"Non-interactive: {FormatBoolValue(state.Environment.IsNonInteractive)}",
            $"Policy skills disabled: {FormatBoolValue(state.Environment.DisablePolicySkills)}",
            string.Empty,
            "Config",
            $"runtime.model: {FormatModel(settings.Runtime.Model)}",
            $"runtime.permissionMode: {settings.Runtime.PermissionMode}",
            $"runtime.enableTelemetry: {FormatEnabledState(settings.Runtime.EnableTelemetry)}",
            $"runtime.fileCheckpointingEnabled: {FormatEnabledState(settings.Runtime.FileCheckpointingEnabled)}",
            $"terminal.useColor: {FormatEnabledState(settings.Terminal.UseColor)}",
            $"terminal.showTimestamps: {FormatEnabledState(settings.Terminal.ShowTimestamps)}",
            $"skipAutoPermissionPrompt: {FormatEnabledState(settings.SkipAutoPermissionPrompt ?? false)}",
            $"useAutoModeDuringPlan: {FormatEnabledState(settings.UseAutoModeDuringPlan ?? false)}",
            $"permissions.allow: {settings.Permissions.Allow.Count}",
            $"permissions.deny: {settings.Permissions.Deny.Count}",
            $"permissions.ask: {settings.Permissions.Ask.Count}",
            $"permissions.additionalDirectories: {settings.Permissions.AdditionalDirectories.Count}",
            $"allowManagedPermissionRulesOnly: {FormatEnabledState(settings.AllowManagedPermissionRulesOnly)}",
            $"disableAllHooks: {FormatEnabledState(settings.DisableAllHooks)}",
            $"allowManagedHooksOnly: {FormatEnabledState(settings.AllowManagedHooksOnly)}",
            $"claudeApiKey: {FormatConfiguredState(settings.ClaudeApiKey)}",
            $"apiKeyHelper: {FormatConfiguredState(settings.ApiKeyHelper)}",
            $"awsCredentialExport: {FormatConfiguredState(settings.AwsCredentialExport)}",
            $"awsAuthRefresh: {FormatConfiguredState(settings.AwsAuthRefresh)}",
            $"forceLoginOrgUUID: {FormatConfiguredState(settings.ForceLoginOrgUUID)}",
            $"otelHeadersHelper: {FormatConfiguredState(settings.OtelHeadersHelper)}",
            $"enabledPlugins: {settings.EnabledPlugins.Count}",
            $"pluginConfigs: {settings.PluginConfigs.Count}",
            string.Empty,
            "Settings Issues"
        };

        if (state.SettingsIssues.Count == 0)
        {
            lines.Add("- None");
        }
        else
        {
            lines.AddRange(state.SettingsIssues.Select(FormatIssue));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string GetSessionName(ConversationSession session)
    {
        return string.IsNullOrWhiteSpace(session.CustomTitle)
            ? "/rename to add a name"
            : session.CustomTitle!;
    }

    private static string FormatModel(string? model)
    {
        var resolved = MainLoopModelResolver.Resolve(model);
        var rendered = MainLoopModelResolver.RenderSetting(model);
        return string.Equals(rendered, resolved, StringComparison.Ordinal)
            ? resolved
            : $"{rendered} ({resolved})";
    }

    private static string FormatIssue(SettingsLoadIssue issue)
    {
        if (string.IsNullOrWhiteSpace(issue.Path))
        {
            return $"- {issue.File}: {issue.Message}";
        }

        return $"- {issue.File} :: {issue.Path}: {issue.Message}";
    }

    private static string FormatBoolValue(bool value)
    {
        return value ? "true" : "false";
    }

    private static string FormatEnabledState(bool value)
    {
        return value ? "enabled" : "disabled";
    }

    private static string FormatConfiguredState(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "unset" : "configured";
    }
}
