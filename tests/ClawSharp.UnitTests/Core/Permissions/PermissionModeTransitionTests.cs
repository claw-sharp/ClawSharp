using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public sealed class PermissionModeTransitionTests
{
    [Fact]
    public void CreateDefault_Uses_Provided_ToolPermissionContext()
    {
        var context = ToolPermissionContexts.CreateEmpty(PermissionMode.Plan) with
        {
            IsBypassPermissionsModeAvailable = true
        };

        var state = ClawSharpAppState.CreateDefault(
            Environment.CurrentDirectory,
            StartupEnvironment.Capture(),
            new ClawSharpSettings(),
            [],
            [],
            [],
            [],
            [],
            [],
            context);

        Assert.Same(context, state.ToolPermissionContext);
    }

    [Fact]
    public void Transition_To_Auto_Strips_Dangerous_Allow_Rules()
    {
        var context = CreateContext(
            mode: PermissionMode.Default,
            alwaysAllowRules: new Dictionary<PermissionRuleSource, IReadOnlyList<string>>
            {
                [PermissionRuleSource.Session] = ["Bash(python:*)", "Read"]
            });

        var transitioned = PermissionModeTransition.Transition(context, PermissionMode.Auto);

        Assert.Equal(PermissionMode.Auto, transitioned.Mode);
        Assert.Equal(["Read"], transitioned.AlwaysAllowRules[PermissionRuleSource.Session]);
        Assert.Equal(["Bash(python:*)"], transitioned.StrippedDangerousRules![PermissionRuleSource.Session]);
    }

    [Fact]
    public void Transition_From_Auto_Restores_Stripped_Rules()
    {
        var context = CreateContext(
            mode: PermissionMode.Auto,
            alwaysAllowRules: new Dictionary<PermissionRuleSource, IReadOnlyList<string>>
            {
                [PermissionRuleSource.Session] = ["Read"]
            }) with
        {
            StrippedDangerousRules = new Dictionary<PermissionRuleSource, IReadOnlyList<string>>
            {
                [PermissionRuleSource.Session] = ["Bash(python:*)"]
            }
        };

        var transitioned = PermissionModeTransition.Transition(context, PermissionMode.Default);

        Assert.Equal(PermissionMode.Default, transitioned.Mode);
        Assert.Equal(["Read", "Bash(python:*)"], transitioned.AlwaysAllowRules[PermissionRuleSource.Session]);
        Assert.Null(transitioned.StrippedDangerousRules);
    }

    [Fact]
    public void Transition_From_Auto_To_Plan_Stores_PrePlanMode_And_Restores_Rules()
    {
        var context = CreateContext(
            mode: PermissionMode.Auto,
            alwaysAllowRules: new Dictionary<PermissionRuleSource, IReadOnlyList<string>>
            {
                [PermissionRuleSource.Session] = ["Read"]
            }) with
        {
            StrippedDangerousRules = new Dictionary<PermissionRuleSource, IReadOnlyList<string>>
            {
                [PermissionRuleSource.Session] = ["Bash(python:*)"]
            }
        };

        var transitioned = PermissionModeTransition.Transition(context, PermissionMode.Plan);

        Assert.Equal(PermissionMode.Plan, transitioned.Mode);
        Assert.Equal(PermissionMode.Auto, transitioned.PrePlanMode);
        Assert.Equal(["Read", "Bash(python:*)"], transitioned.AlwaysAllowRules[PermissionRuleSource.Session]);
        Assert.Null(transitioned.StrippedDangerousRules);
    }

    [Fact]
    public void Leaving_Plan_To_Default_Restores_PrePlanMode()
    {
        var context = CreateContext(
            mode: PermissionMode.Plan,
            alwaysAllowRules: new Dictionary<PermissionRuleSource, IReadOnlyList<string>>
            {
                [PermissionRuleSource.Session] = ["Read", "Bash(python:*)"]
            }) with
        {
            PrePlanMode = PermissionMode.AcceptEdits
        };

        var transitioned = PermissionModeTransition.Transition(context, PermissionMode.Default);

        Assert.Equal(PermissionMode.AcceptEdits, transitioned.Mode);
        Assert.Null(transitioned.PrePlanMode);
    }

    [Fact]
    public void CreateDisabledBypassPermissionsContext_Removes_Availability_And_Falls_Back_To_Default()
    {
        var context = CreateContext(mode: PermissionMode.BypassPermissions) with
        {
            IsBypassPermissionsModeAvailable = true
        };

        var transitioned = PermissionModeTransition.CreateDisabledBypassPermissionsContext(context);

        Assert.Equal(PermissionMode.Default, transitioned.Mode);
        Assert.False(transitioned.IsBypassPermissionsModeAvailable);
    }

    [Fact]
    public void AppStateMutation_Uses_Transitioned_Mode_Instead_Of_Raw_Target()
    {
        var state = ClawSharpAppState.CreateDefault(
            Environment.CurrentDirectory,
            StartupEnvironment.Capture(),
            new ClawSharpSettings(),
            [],
            [],
            [],
            [],
            [],
            [],
            CreateContext(mode: PermissionMode.Plan) with
            {
                PrePlanMode = PermissionMode.AcceptEdits
            });

        var mutated = ClawSharpAppStateMutations.WithToolPermissionMode(state, PermissionMode.Default);

        Assert.Equal(PermissionMode.AcceptEdits, mutated.ToolPermissionContext.Mode);
        Assert.Equal(PermissionMode.AcceptEdits, mutated.Settings.Runtime.PermissionMode);
    }

    [Fact]
    public void PermissionContextBootstrapper_Loads_Mode_And_Disables_Bypass_When_Configured()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-permission-bootstrap", Guid.NewGuid().ToString("N"));
        var configDir = Path.Combine(Path.GetTempPath(), "clawsharp-permission-config", Guid.NewGuid().ToString("N"));
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");

        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(configDir);
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDir);

        try
        {
            Directory.CreateDirectory(Path.Combine(workspaceRoot, ".clawsharp"));
            File.WriteAllText(
                ClaudeConfigPaths.GetProjectSettingsFilePath(workspaceRoot),
                """
                {
                  "permissions": {
                    "defaultMode": "bypassPermissions",
                    "allow": ["Bash(echo:*)"],
                    "disableBypassPermissionsMode": "disable"
                  }
                }
                """);

            var context = new PermissionContextBootstrapper().Load(workspaceRoot, new ClawSharpSettings());

            Assert.Equal(PermissionMode.Default, context.Mode);
            Assert.False(context.IsBypassPermissionsModeAvailable);
            Assert.Contains(
                context.AlwaysAllowRules.Values.SelectMany(static rules => rules),
                static rule => rule == "Bash(echo:*)");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", originalConfigDir);
            DeleteDirectoryIfExists(workspaceRoot);
            DeleteDirectoryIfExists(configDir);
        }
    }

    [Fact]
    public async Task PermissionContextBootstrapper_Loads_AutoModeAvailability_And_TrustedPlanPolicy()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-auto-mode-bootstrap", Guid.NewGuid().ToString("N"));
        var configDir = Path.Combine(Path.GetTempPath(), "clawsharp-auto-mode-config", Guid.NewGuid().ToString("N"));
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");

        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(configDir);
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", configDir);

        try
        {
            await File.WriteAllTextAsync(
                ClaudeConfigPaths.GetUserSettingsFilePath(),
                """
                {
                  "skipAutoPermissionPrompt": true,
                  "runtime": {
                    "model": "claude-sonnet-4-6"
                  }
                }
                """);
            Directory.CreateDirectory(Path.Combine(workspaceRoot, ".clawsharp"));
            await File.WriteAllTextAsync(
                ClaudeConfigPaths.GetProjectSettingsFilePath(workspaceRoot),
                """
                {
                  "useAutoModeDuringPlan": false
                }
                """);
            await File.WriteAllTextAsync(
                ClaudeConfigPaths.GetLocalSettingsFilePath(workspaceRoot),
                """
                {
                  "useAutoModeDuringPlan": false
                }
                """);

            var settingsResult = await new SettingsBootstrapper().LoadAsync(workspaceRoot);
            var context = new PermissionContextBootstrapper().Load(
                workspaceRoot,
                settingsResult.Settings,
                settingsResult.SourcePreferences);

            Assert.True(context.IsAutoModeAvailable);
            Assert.False(context.UseAutoModeDuringPlan);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", originalConfigDir);
            DeleteDirectoryIfExists(workspaceRoot);
            DeleteDirectoryIfExists(configDir);
        }
    }

    [Fact]
    public void PermissionContextBootstrapper_FallsBack_FromAuto_WhenGateIsUnavailable()
    {
        var settings = new ClawSharpSettings
        {
            Runtime = new RuntimeSettings
            {
                PermissionMode = PermissionMode.Auto,
                Model = "claude-sonnet-4-5"
            },
            Permissions = new PermissionSettings
            {
                DefaultMode = PermissionMode.Auto
            }
        };

        var context = new PermissionContextBootstrapper().Load(
            Environment.CurrentDirectory,
            settings,
            new Dictionary<PermissionRuleSource, SettingsSourcePreferences>
            {
                [PermissionRuleSource.UserSettings] = new(SkipAutoPermissionPrompt: true)
            });

        Assert.Equal(PermissionMode.Default, context.Mode);
        Assert.False(context.IsAutoModeAvailable);
    }

    private static ToolPermissionContext CreateContext(
        PermissionMode mode = PermissionMode.Default,
        IReadOnlyDictionary<PermissionRuleSource, IReadOnlyList<string>>? alwaysAllowRules = null)
    {
        return new ToolPermissionContext(
            mode,
            new Dictionary<string, AdditionalWorkingDirectory>(StringComparer.Ordinal),
            alwaysAllowRules ?? new Dictionary<PermissionRuleSource, IReadOnlyList<string>>(),
            new Dictionary<PermissionRuleSource, IReadOnlyList<string>>(),
            new Dictionary<PermissionRuleSource, IReadOnlyList<string>>(),
            IsBypassPermissionsModeAvailable: false);
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(50);
            }
        }
    }
}
