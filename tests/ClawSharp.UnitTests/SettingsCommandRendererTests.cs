using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Tasks;
using ClawSharp.Ui.Terminal;

namespace ClawSharp.UnitTests;

public class SettingsCommandRendererTests
{
    [Fact]
    public void Render_Includes_Status_And_Config_Sections_From_Current_CSharp_Data()
    {
        var repoRoot = OperatingSystem.IsWindows() ? @"D:\repo" : "/repo";
        var configRoot = OperatingSystem.IsWindows() ? @"D:\config" : "/config";
        
        var renderer = new SettingsCommandRenderer();
        var settings = new ClawSharpSettings
        {
            Runtime = new RuntimeSettings
            {
                PermissionMode = PermissionMode.Plan,
                Model = "claude-sonnet-4-5-20250929",
                EnableTelemetry = true,
                FileCheckpointingEnabled = false
            },
            Terminal = new TerminalSettings
            {
                UseColor = false,
                ShowTimestamps = true
            },
            SkipAutoPermissionPrompt = true,
            UseAutoModeDuringPlan = true,
            ClaudeApiKey = "claude-key",
            ApiKeyHelper = "helper",
            AwsCredentialExport = "aws export",
            AwsAuthRefresh = "aws refresh",
            ForceLoginOrgUUID = "org-1",
            OtelHeadersHelper = "otel helper",
            AllowManagedPermissionRulesOnly = true,
            DisableAllHooks = false,
            AllowManagedHooksOnly = true,
            Permissions = new PermissionSettings
            {
                Allow = ["Read"],
                Deny = ["Shell"],
                Ask = ["Write"],
                AdditionalDirectories = [Path.Combine(repoRoot, "docs")]
            },
            EnabledPlugins = new Dictionary<string, PluginEnabledSetting>(StringComparer.Ordinal)
            {
                ["plugin-a"] = new PluginEnabledSetting { Enabled = true }
            },
            PluginConfigs = new Dictionary<string, PluginConfigSettings>(StringComparer.Ordinal)
            {
                ["plugin-a"] = new PluginConfigSettings()
            }
        };

        var session = new ConversationSession("session-1", repoRoot);
        session.SetCustomTitle("Incident Review");

        var state = ClawSharpAppState.CreateDefault(
            repoRoot,
            new StartupEnvironment(configRoot, BareMode: true, DisablePolicySkills: true, IsNonInteractive: true),
            settings,
            [],
            [
                new DiscoveredPluginInstallation("plugin-a", PluginInstallationScope.Local, Path.Combine(repoRoot, ".plugins", "plugin-a"), repoRoot, "1.0.0", null, null, null)
            ],
            [],
            [],
            [],
            [],
            ToolPermissionContexts.CreateEmpty(PermissionMode.AcceptEdits)) with
        {
            MainLoopModel = "gpt-5.4",
            Tasks = new Dictionary<string, ClawSharpTask>(StringComparer.Ordinal)
            {
                ["task-1"] = new LocalBashTask(
                    "task-1",
                    "Run build",
                    ClawSharp.Tasks.TaskStatus.Running,
                    DateTimeOffset.UtcNow,
                    "task.log",
                    "dotnet build")
            }
        };

        var rendered = renderer.Render(CreateContext(settings, state, session, repoRoot));

        Assert.Contains("Settings", rendered, StringComparison.Ordinal);
        Assert.Contains("Status", rendered, StringComparison.Ordinal);
        Assert.Contains("Version: ClawSharp 0.0.5", rendered, StringComparison.Ordinal);
        Assert.Contains("Session name: Incident Review", rendered, StringComparison.Ordinal);
        Assert.Contains("Session ID: session-1", rendered, StringComparison.Ordinal);
        Assert.Contains($"cwd: {repoRoot}", rendered, StringComparison.Ordinal);
        var providerConfig = ProviderRuntimeResolver.Resolve(settings, state.MainLoopModel ?? settings.Runtime.Model);
        Assert.Contains($"Provider: {providerConfig.Provider}", rendered, StringComparison.Ordinal);
        Assert.Contains($"Provider endpoint: {providerConfig.BaseUrl}", rendered, StringComparison.Ordinal);
        Assert.Contains("Model: GPT-5.4", rendered, StringComparison.Ordinal);
        Assert.Contains("Active permission mode: AcceptEdits", rendered, StringComparison.Ordinal);
        Assert.Contains("Background tasks: 1", rendered, StringComparison.Ordinal);
        Assert.Contains("Plugin installations: 1", rendered, StringComparison.Ordinal);
        Assert.Contains("Bare mode: true", rendered, StringComparison.Ordinal);
        Assert.Contains("Policy skills disabled: true", rendered, StringComparison.Ordinal);
        Assert.Contains("Config", rendered, StringComparison.Ordinal);
        Assert.Contains("runtime.model: Sonnet 4.5 (claude-sonnet-4-5-20250929)", rendered, StringComparison.Ordinal);
        Assert.Contains("runtime.permissionMode: Plan", rendered, StringComparison.Ordinal);
        Assert.Contains("runtime.enableTelemetry: enabled", rendered, StringComparison.Ordinal);
        Assert.Contains("runtime.fileCheckpointingEnabled: disabled", rendered, StringComparison.Ordinal);
        Assert.Contains("terminal.useColor: disabled", rendered, StringComparison.Ordinal);
        Assert.Contains("terminal.showTimestamps: enabled", rendered, StringComparison.Ordinal);
        Assert.Contains("skipAutoPermissionPrompt: enabled", rendered, StringComparison.Ordinal);
        Assert.Contains("useAutoModeDuringPlan: enabled", rendered, StringComparison.Ordinal);
        Assert.Contains("permissions.allow: 1", rendered, StringComparison.Ordinal);
        Assert.Contains("permissions.deny: 1", rendered, StringComparison.Ordinal);
        Assert.Contains("permissions.ask: 1", rendered, StringComparison.Ordinal);
        Assert.Contains("permissions.additionalDirectories: 1", rendered, StringComparison.Ordinal);
        Assert.Contains("allowManagedPermissionRulesOnly: enabled", rendered, StringComparison.Ordinal);
        Assert.Contains("disableAllHooks: disabled", rendered, StringComparison.Ordinal);
        Assert.Contains("allowManagedHooksOnly: enabled", rendered, StringComparison.Ordinal);
        Assert.Contains("claudeApiKey: configured", rendered, StringComparison.Ordinal);
        Assert.Contains("apiKeyHelper: configured", rendered, StringComparison.Ordinal);
        Assert.Contains("awsCredentialExport: configured", rendered, StringComparison.Ordinal);
        Assert.Contains("awsAuthRefresh: configured", rendered, StringComparison.Ordinal);
        Assert.Contains("forceLoginOrgUUID: configured", rendered, StringComparison.Ordinal);
        Assert.Contains("otelHeadersHelper: configured", rendered, StringComparison.Ordinal);
        Assert.Contains("enabledPlugins: 1", rendered, StringComparison.Ordinal);
        Assert.Contains("pluginConfigs: 1", rendered, StringComparison.Ordinal);
        Assert.Contains("Settings Issues", rendered, StringComparison.Ordinal);
        Assert.Contains("- None", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_Uses_Rename_Hint_And_Lists_Settings_Issues()
    {
        var repoPath = OperatingSystem.IsWindows() ? @"D:\repo" : "/repo";
        var configPath = OperatingSystem.IsWindows() ? @"D:\config" : "/config";
        var settingsPath = Path.Combine(repoPath, ".claude", "settings.json");

        var renderer = new SettingsCommandRenderer();
        var settings = new ClawSharpSettings();
        var session = new ConversationSession("session-2", repoPath);
        var issue = new SettingsLoadIssue(
            settingsPath,
            "runtime.model",
            "Expected string value.");
        var state = ClawSharpAppState.CreateDefault(
            repoPath,
            new StartupEnvironment(configPath),
            settings,
            [issue],
            [],
            [],
            [],
            [],
            []);

        var rendered = renderer.Render(CreateContext(settings, state, session, repoPath));

        Assert.Contains("Session name: /rename to add a name", rendered, StringComparison.Ordinal);
        Assert.Contains("Settings Issues", rendered, StringComparison.Ordinal);
        Assert.Contains($"- {settingsPath} :: runtime.model: Expected string value.", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("- None", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handler_Uses_Renderer_Output_For_Config_Command()
    {
        var repoRoot = OperatingSystem.IsWindows() ? @"D:\repo" : "/repo";
        var configRoot = OperatingSystem.IsWindows() ? @"D:\config" : "/config";
        
        var handler = new SettingsCommandHandler();
        var settings = new ClawSharpSettings();
        var state = ClawSharpAppState.CreateDefault(
            repoRoot,
            new StartupEnvironment(configRoot),
            settings,
            [],
            [],
            [],
            [],
            [],
            []);
        var context = CreateContext(settings, state, new ConversationSession("session-3", repoRoot), repoRoot);

        var result = await handler.ExecuteAsync("/config", context);

        Assert.True(result.Success);
        Assert.Contains("Settings", result.Output, StringComparison.Ordinal);
        Assert.Contains("Status", result.Output, StringComparison.Ordinal);
        Assert.Contains("Config", result.Output, StringComparison.Ordinal);
    }

    private static CommandExecutionContext CreateContext(
        ClawSharpSettings settings,
        ClawSharpAppState state,
        ConversationSession session,
        string repoRoot)
    {
        return new CommandExecutionContext
        {
            AppStateStore = new ClawSharpAppStateStore(state),
            Session = session,
            SessionFactory = new DefaultSessionFactory(repoRoot),
            TranscriptStore = new JsonlTranscriptStore(),
            Settings = settings
        };
    }
}
