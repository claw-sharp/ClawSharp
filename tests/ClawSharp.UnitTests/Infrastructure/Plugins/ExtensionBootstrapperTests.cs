using System.Text;
using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class ExtensionBootstrapperTests
{
    [Fact]
    public async Task LoadAsync_Discovers_Managed_User_And_Project_Skills_In_Ts_Order()
    {
        var tempRoot = CreateTempDirectory();
        var workspaceRoot = Path.Combine(tempRoot, "repo", "child");
        var managedRoot = Path.Combine(tempRoot, "managed");
        var userConfigHomeDir = Path.Combine(tempRoot, "user");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(Path.Combine(tempRoot, "repo", ".git"));

        CreateSkill(Path.Combine(managedRoot, ".claude", "skills"), "managed-skill");
        CreateSkill(Path.Combine(userConfigHomeDir, "skills"), "user-skill");
        CreateSkill(Path.Combine(tempRoot, "repo", ".claude", "skills"), "root-skill");
        CreateSkill(Path.Combine(workspaceRoot, ".claude", "skills"), "child-skill");

        var bootstrapper = new ExtensionBootstrapper(
            managedFilePath: managedRoot,
            userConfigHomeDir: userConfigHomeDir,
            pluginsDirectoryPath: Path.Combine(tempRoot, "plugins"));

        var result = await bootstrapper.LoadAsync(
            workspaceRoot,
            new StartupEnvironment(userConfigHomeDir, BareMode: false, DisablePolicySkills: false),
            new ClawSharpSettings());

        Assert.Equal(
            ["managed-skill", "user-skill", "child-skill", "root-skill"],
            result.Skills.Select(static skill => skill.Name).ToArray());
        Assert.Equal(
            ["policySettings", "userSettings", "projectSettings", "projectSettings"],
            result.Skills.Select(static skill => skill.Source).ToArray());
    }

    [Fact]
    public async Task LoadAsync_Skips_Skill_Autodiscovery_In_Bare_Mode()
    {
        var tempRoot = CreateTempDirectory();
        var workspaceRoot = Path.Combine(tempRoot, "repo");
        var managedRoot = Path.Combine(tempRoot, "managed");
        var userConfigHomeDir = Path.Combine(tempRoot, "user");
        Directory.CreateDirectory(workspaceRoot);

        CreateSkill(Path.Combine(managedRoot, ".claude", "skills"), "managed-skill");
        CreateSkill(Path.Combine(userConfigHomeDir, "skills"), "user-skill");
        CreateSkill(Path.Combine(workspaceRoot, ".claude", "skills"), "project-skill");

        var bootstrapper = new ExtensionBootstrapper(
            managedFilePath: managedRoot,
            userConfigHomeDir: userConfigHomeDir,
            pluginsDirectoryPath: Path.Combine(tempRoot, "plugins"));

        var result = await bootstrapper.LoadAsync(
            workspaceRoot,
            new StartupEnvironment(userConfigHomeDir, BareMode: true, DisablePolicySkills: false),
            new ClawSharpSettings());

        Assert.Empty(result.Skills);
    }

    [Fact]
    public async Task LoadAsync_Reads_Installed_Plugins_V2_And_Converts_V1_To_User_Scope()
    {
        var tempRoot = CreateTempDirectory();
        var pluginsDirectoryPath = Path.Combine(tempRoot, "plugins");
        Directory.CreateDirectory(pluginsDirectoryPath);
        var installedPluginsPath = Path.Combine(pluginsDirectoryPath, "installed_plugins.json");

        File.WriteAllText(
            installedPluginsPath,
            """
            {
              "version": 1,
              "plugins": {
                "formatter@anthropic-tools": {
                  "version": "1.2.3",
                  "installedAt": "2026-04-01T10:00:00Z",
                  "lastUpdated": "2026-04-01T11:00:00Z",
                  "installPath": "ignored-by-v2-conversion"
                }
              }
            }
            """,
            Encoding.UTF8);

        var bootstrapper = new ExtensionBootstrapper(
            managedFilePath: Path.Combine(tempRoot, "managed"),
            userConfigHomeDir: Path.Combine(tempRoot, "user"),
            pluginsDirectoryPath: pluginsDirectoryPath);

        var result = await bootstrapper.LoadAsync(
            Path.Combine(tempRoot, "repo"),
            new StartupEnvironment(Path.Combine(tempRoot, "user"), BareMode: false, DisablePolicySkills: false),
            new ClawSharpSettings());

        var installation = Assert.Single(result.PluginInstallations);
        Assert.Equal("formatter@anthropic-tools", installation.PluginId);
        Assert.Equal(PluginInstallationScope.User, installation.Scope);
        Assert.Equal(
            Path.Combine(pluginsDirectoryPath, "cache", "anthropic-tools", "formatter", "1.2.3"),
            installation.InstallPath);
        Assert.Equal("1.2.3", installation.Version);
        Assert.Equal("2026-04-01T10:00:00Z", installation.InstalledAt);
        Assert.Equal("2026-04-01T11:00:00Z", installation.LastUpdated);
    }

    [Fact]
    public async Task LoadAsync_Loads_Plugin_Manifest_Hooks_And_Plugin_Skills()
    {
        var tempRoot = CreateTempDirectory();
        var pluginsDirectoryPath = Path.Combine(tempRoot, "plugins");
        var pluginInstallPath = Path.Combine(tempRoot, "cache", "formatter");
        Directory.CreateDirectory(pluginsDirectoryPath);
        Directory.CreateDirectory(pluginInstallPath);
        Directory.CreateDirectory(Path.Combine(pluginInstallPath, "hooks"));
        CreateSkill(Path.Combine(pluginInstallPath, "skills"), "plugin-skill");
        File.WriteAllText(
            Path.Combine(pluginInstallPath, "plugin.json"),
            """
            {
              "name": "formatter",
              "description": "Formats files",
              "version": "1.2.3",
              "skills": ["./skills"],
              "hooks": ["./extra-hooks.json"]
            }
            """,
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(pluginInstallPath, "hooks", "hooks.json"),
            """
            {
              "description": "plugin hooks",
              "hooks": {
                "PreToolUse": [
                  {
                    "matcher": "Write",
                    "hooks": [
                      { "type": "command", "command": "echo before-write", "timeout": 5 }
                    ]
                  }
                ]
              }
            }
            """,
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(pluginInstallPath, "extra-hooks.json"),
            """
            {
              "PostToolUse": [
                {
                  "hooks": [
                    { "type": "http", "url": "https://example.test/hook", "timeout": 10 }
                  ]
                }
              ]
            }
            """,
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(pluginsDirectoryPath, "installed_plugins.json"),
            $$"""
            {
              "version": 2,
              "plugins": {
                "formatter@anthropic-tools": [
                  {
                    "scope": "user",
                    "installPath": "{{pluginInstallPath.Replace("\\", "\\\\", StringComparison.Ordinal)}}",
                    "version": "1.2.3"
                  }
                ]
              }
            }
            """,
            Encoding.UTF8);

        var bootstrapper = new ExtensionBootstrapper(
            managedFilePath: Path.Combine(tempRoot, "managed"),
            userConfigHomeDir: Path.Combine(tempRoot, "user"),
            pluginsDirectoryPath: pluginsDirectoryPath);

        var result = await bootstrapper.LoadAsync(
            Path.Combine(tempRoot, "repo"),
            new StartupEnvironment(Path.Combine(tempRoot, "user"), BareMode: false, DisablePolicySkills: false),
            new ClawSharpSettings());

        var plugin = Assert.Single(result.Plugins);
        Assert.True(plugin.Enabled);
        Assert.Equal("formatter", plugin.Name);
        Assert.Equal("1.2.3", plugin.Manifest!.Version);
        Assert.True(plugin.Hooks.ContainsKey(HookEvent.PreToolUse));
        Assert.True(plugin.Hooks.ContainsKey(HookEvent.PostToolUse));
        Assert.Contains(result.Skills, static skill => skill.Name == "plugin-skill" && skill.Source == "plugin");
        Assert.Contains(result.Hooks, static hook => hook.PluginId == "formatter@anthropic-tools" && hook.Event == HookEvent.PreToolUse);
    }

    [Fact]
    public async Task LoadAsync_Disables_Plugin_When_Settings_Entry_Is_False()
    {
        var tempRoot = CreateTempDirectory();
        var pluginsDirectoryPath = Path.Combine(tempRoot, "plugins");
        var pluginInstallPath = Path.Combine(tempRoot, "cache", "formatter");
        Directory.CreateDirectory(pluginsDirectoryPath);
        Directory.CreateDirectory(pluginInstallPath);
        File.WriteAllText(
            Path.Combine(pluginInstallPath, "plugin.json"),
            """{ "name": "formatter" }""",
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(pluginsDirectoryPath, "installed_plugins.json"),
            $$"""
            {
              "version": 2,
              "plugins": {
                "formatter@anthropic-tools": [
                  {
                    "scope": "user",
                    "installPath": "{{pluginInstallPath.Replace("\\", "\\\\", StringComparison.Ordinal)}}"
                  }
                ]
              }
            }
            """,
            Encoding.UTF8);

        var bootstrapper = new ExtensionBootstrapper(
            managedFilePath: Path.Combine(tempRoot, "managed"),
            userConfigHomeDir: Path.Combine(tempRoot, "user"),
            pluginsDirectoryPath: pluginsDirectoryPath);

        var result = await bootstrapper.LoadAsync(
            Path.Combine(tempRoot, "repo"),
            new StartupEnvironment(Path.Combine(tempRoot, "user"), BareMode: false, DisablePolicySkills: false),
            new ClawSharpSettings
            {
                EnabledPlugins = new Dictionary<string, PluginEnabledSetting>(StringComparer.Ordinal)
                {
                    ["formatter@anthropic-tools"] = new PluginEnabledSetting { Enabled = false }
                }
            });

        Assert.False(Assert.Single(result.Plugins).Enabled);
    }

    [Fact]
    public async Task LoadAsync_Prefers_SettingsJson_For_Allowlisted_Plugin_Settings_And_Parses_UserConfig()
    {
        var tempRoot = CreateTempDirectory();
        var pluginsDirectoryPath = Path.Combine(tempRoot, "plugins");
        var pluginInstallPath = Path.Combine(tempRoot, "cache", "reviewer");
        Directory.CreateDirectory(pluginsDirectoryPath);
        Directory.CreateDirectory(pluginInstallPath);
        File.WriteAllText(
            Path.Combine(pluginInstallPath, "plugin.json"),
            """
            {
              "name": "reviewer",
              "settings": {
                "agent": "manifest-agent",
                "ignored": "value"
              },
              "userConfig": {
                "apiKey": {
                  "type": "string",
                  "title": "API key",
                  "description": "Token",
                  "required": true,
                  "sensitive": true
                }
              }
            }
            """,
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(pluginInstallPath, "settings.json"),
            """
            {
              "agent": "settings-agent",
              "ignored": "settings-json-only"
            }
            """,
            Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(pluginsDirectoryPath, "installed_plugins.json"),
            $$"""
            {
              "version": 2,
              "plugins": {
                "reviewer@anthropic-tools": [
                  {
                    "scope": "user",
                    "installPath": "{{pluginInstallPath.Replace("\\", "\\\\", StringComparison.Ordinal)}}"
                  }
                ]
              }
            }
            """,
            Encoding.UTF8);

        var bootstrapper = new ExtensionBootstrapper(
            managedFilePath: Path.Combine(tempRoot, "managed"),
            userConfigHomeDir: Path.Combine(tempRoot, "user"),
            pluginsDirectoryPath: pluginsDirectoryPath);

        var result = await bootstrapper.LoadAsync(
            Path.Combine(tempRoot, "repo"),
            new StartupEnvironment(Path.Combine(tempRoot, "user"), BareMode: false, DisablePolicySkills: false),
            new ClawSharpSettings());

        var plugin = Assert.Single(result.Plugins);
        Assert.Equal("settings-agent", plugin.Settings!["agent"]);
        Assert.Equal("manifest-agent", plugin.Manifest!.Settings!["agent"]);
        Assert.False(plugin.Settings.ContainsKey("ignored"));
        var option = Assert.Single(plugin.UserConfig!);
        Assert.Equal("apiKey", option.Key);
        Assert.True(option.Value.Sensitive);
        Assert.True(option.Value.Required);
    }

    [Fact]
    public async Task LoadAsync_Loads_BuiltIn_Plugins_Using_Builtin_Enablement_Semantics()
    {
        var tempRoot = CreateTempDirectory();
        var builtinRoot = Path.Combine(tempRoot, "builtin-reviewer");
        Directory.CreateDirectory(Path.Combine(builtinRoot, "skills", "reviewer-skill"));
        File.WriteAllText(Path.Combine(builtinRoot, "skills", "reviewer-skill", "SKILL.md"), "# reviewer-skill", Encoding.UTF8);

        var registry = new BuiltInPluginRegistry(
        [
            new BuiltInPluginDefinition(
                "reviewer",
                "Built in reviewer",
                builtinRoot,
                DefaultEnabled: false,
                SkillDirectories: [Path.Combine(builtinRoot, "skills")])
        ]);
        var bootstrapper = new ExtensionBootstrapper(
            managedFilePath: Path.Combine(tempRoot, "managed"),
            userConfigHomeDir: Path.Combine(tempRoot, "user"),
            pluginsDirectoryPath: Path.Combine(tempRoot, "plugins"),
            builtInPluginRegistry: registry);

        var disabledResult = await bootstrapper.LoadAsync(
            Path.Combine(tempRoot, "repo"),
            new StartupEnvironment(Path.Combine(tempRoot, "user"), BareMode: false, DisablePolicySkills: false),
            new ClawSharpSettings
            {
                EnabledPlugins = new Dictionary<string, PluginEnabledSetting>(StringComparer.Ordinal)
                {
                    ["reviewer@builtin"] = new PluginEnabledSetting { VersionConstraints = ["^1.0.0"] }
                }
            });
        Assert.False(Assert.Single(disabledResult.Plugins).Enabled);

        var enabledResult = await bootstrapper.LoadAsync(
            Path.Combine(tempRoot, "repo"),
            new StartupEnvironment(Path.Combine(tempRoot, "user"), BareMode: false, DisablePolicySkills: false),
            new ClawSharpSettings
            {
                EnabledPlugins = new Dictionary<string, PluginEnabledSetting>(StringComparer.Ordinal)
                {
                    ["reviewer@builtin"] = new PluginEnabledSetting { Enabled = true }
                }
            });

        var plugin = Assert.Single(enabledResult.Plugins);
        Assert.True(plugin.Enabled);
        Assert.Equal("reviewer@builtin", plugin.PluginId);
        Assert.Contains(enabledResult.Skills, static skill => skill.Name == "reviewer-skill" && skill.Source == "plugin");
    }

    private static void CreateSkill(string skillsDirectory, string name)
    {
        var skillDirectory = Path.Combine(skillsDirectory, name);
        Directory.CreateDirectory(skillDirectory);
        File.WriteAllText(Path.Combine(skillDirectory, "SKILL.md"), $"# {name}", Encoding.UTF8);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "clawsharp-extension-bootstrapper-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
