using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public class SettingsBootstrapperTests
{
    [Fact]
    public async Task LoadAsync_Merges_Settings_In_Ts_Precedence_Order()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-settings-bootstrap", Guid.NewGuid().ToString("N"));
        var configDir = Path.Combine(Path.GetTempPath(), "clawsharp-settings-config", Guid.NewGuid().ToString("N"));
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
                  "claudeApiKey": "user-key",
                  "runtime": {
                    "model": "user-model",
                    "permissionMode": "plan"
                  },
                  "terminal": {
                    "useColor": false
                  }
                }
                """);
            Directory.CreateDirectory(Path.Combine(workspaceRoot, ".clawsharp"));
            await File.WriteAllTextAsync(
                ClaudeConfigPaths.GetProjectSettingsFilePath(workspaceRoot),
                """
                {
                  "runtime": {
                    "model": "project-model"
                  },
                  "terminal": {
                    "showTimestamps": true
                  }
                }
                """);
            await File.WriteAllTextAsync(
                ClaudeConfigPaths.GetLocalSettingsFilePath(workspaceRoot),
                """
                {
                  "runtime": {
                    "enableTelemetry": true,
                    "permissionMode": "bypassPermissions",
                    "fileCheckpointingEnabled": false,
                    "autoMemoryEnabled": false,
                    "autoMemoryDirectory": "~/memory-override"
                  }
                }
                """);

            var result = await new SettingsBootstrapper().LoadAsync(workspaceRoot);

            Assert.Empty(result.Issues);
            Assert.Equal("user-key", result.Settings.ClaudeApiKey);
            Assert.Equal("project-model", result.Settings.Runtime.Model);
            Assert.True(result.Settings.Runtime.EnableTelemetry);
            Assert.False(result.Settings.Runtime.AutoMemoryEnabled);
            Assert.Equal("~/memory-override", result.Settings.Runtime.AutoMemoryDirectory);
            Assert.True(result.Settings.Terminal.ShowTimestamps);
            Assert.Equal(3, result.SourcePreferences.Count);
            Assert.All(result.SourcePreferences.Values, static preferences =>
            {
                Assert.Null(preferences.SkipAutoPermissionPrompt);
                Assert.Null(preferences.UseAutoModeDuringPlan);
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", originalConfigDir);

            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }

            if (Directory.Exists(configDir))
            {
                Directory.Delete(configDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LoadAsync_Reads_Extension_Settings_With_Ts_Shaped_Types()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-settings-bootstrap", Guid.NewGuid().ToString("N"));
        var configDir = Path.Combine(Path.GetTempPath(), "clawsharp-settings-config", Guid.NewGuid().ToString("N"));
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
                  "enabledPlugins": {
                    "formatter@anthropic-tools": true
                  },
                  "pluginConfigs": {
                    "formatter@anthropic-tools": {
                      "options": {
                        "style": "standard"
                      }
                    }
                  },
                  "hooks": {
                    "PreToolUse": [
                      {
                        "matcher": "Write",
                        "hooks": [
                          { "type": "command", "command": "echo before" }
                        ]
                      }
                    ]
                  }
                }
                """);
            Directory.CreateDirectory(Path.Combine(workspaceRoot, ".clawsharp"));
            await File.WriteAllTextAsync(
                ClaudeConfigPaths.GetProjectSettingsFilePath(workspaceRoot),
                """
                {
                  "enabledPlugins": {
                    "formatter@anthropic-tools": ["^1.2.3"]
                  },
                  "hooks": {
                    "PreToolUse": [
                      {
                        "matcher": "*",
                        "hooks": [
                          { "type": "http", "url": "https://example.test/hook" }
                        ]
                      }
                    ]
                  }
                }
                """);

            var result = await new SettingsBootstrapper().LoadAsync(workspaceRoot);

            Assert.Empty(result.Issues);
            Assert.True(result.Settings.EnabledPlugins.TryGetValue("formatter@anthropic-tools", out var pluginSetting));
            Assert.Equal(["^1.2.3"], pluginSetting!.VersionConstraints);
            Assert.True(result.Settings.PluginConfigs.ContainsKey("formatter@anthropic-tools"));
            Assert.True(result.Settings.Hooks.ContainsKey(HookEvent.PreToolUse));
            Assert.Equal(2, result.Settings.Hooks[HookEvent.PreToolUse].Count);
            Assert.Equal(HookKind.Command, result.Settings.Hooks[HookEvent.PreToolUse][0].Hooks[0].Type);
            Assert.Equal(HookKind.Http, result.Settings.Hooks[HookEvent.PreToolUse][1].Hooks[0].Type);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", originalConfigDir);

            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }

            if (Directory.Exists(configDir))
            {
                Directory.Delete(configDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LoadAsync_Reads_Agent_Model_Routing_Settings()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-settings-bootstrap", Guid.NewGuid().ToString("N"));
        var configDir = Path.Combine(Path.GetTempPath(), "clawsharp-settings-config", Guid.NewGuid().ToString("N"));
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
                  "agentModels": {
                    "gpt-4o": {
                      "baseUrl": "https://api.openai.com/v1",
                      "apiKey": "sk-test"
                    }
                  },
                  "agentRouting": {
                    "Explore": "gpt-4o",
                    "default": "gpt-4o"
                  }
                }
                """);

            var result = await new SettingsBootstrapper().LoadAsync(workspaceRoot);

            Assert.Empty(result.Issues);
            Assert.Equal("https://api.openai.com/v1", result.Settings.AgentModels["gpt-4o"].BaseUrl);
            Assert.Equal("sk-test", result.Settings.AgentModels["gpt-4o"].ApiKey);
            Assert.Equal("gpt-4o", result.Settings.AgentRouting["Explore"]);
            Assert.Equal("gpt-4o", result.Settings.AgentRouting["default"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", originalConfigDir);

            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }

            if (Directory.Exists(configDir))
            {
                Directory.Delete(configDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LoadAsync_Reads_Attribution_Settings()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-settings-bootstrap", Guid.NewGuid().ToString("N"));
        var configDir = Path.Combine(Path.GetTempPath(), "clawsharp-settings-config", Guid.NewGuid().ToString("N"));
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
                  "attribution": {
                    "commit": "main",
                    "pr": "123"
                  }
                }
                """);

            var result = await new SettingsBootstrapper().LoadAsync(workspaceRoot);

            Assert.Empty(result.Issues);
            Assert.NotNull(result.Settings.Attribution);
            Assert.Equal("main", result.Settings.Attribution!.Commit);
            Assert.Equal("123", result.Settings.Attribution.Pr);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", originalConfigDir);

            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }

            if (Directory.Exists(configDir))
            {
                Directory.Delete(configDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LoadAsync_Ignores_Unknown_Settings_Properties()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-settings-bootstrap", Guid.NewGuid().ToString("N"));
        var configDir = Path.Combine(Path.GetTempPath(), "clawsharp-settings-config", Guid.NewGuid().ToString("N"));
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
                  "runtime": {
                    "model": "user-model"
                  },
                  "futureSetting": {
                    "enabled": true
                  }
                }
                """);

            var result = await new SettingsBootstrapper().LoadAsync(workspaceRoot);

            Assert.Empty(result.Issues);
            Assert.Equal("user-model", result.Settings.Runtime.Model);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", originalConfigDir);

            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }

            if (Directory.Exists(configDir))
            {
                Directory.Delete(configDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LoadAsync_Reports_Invalid_File_And_Continues()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-settings-bootstrap", Guid.NewGuid().ToString("N"));
        var configDir = Path.Combine(Path.GetTempPath(), "clawsharp-settings-config", Guid.NewGuid().ToString("N"));
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
                  "runtime": {
                    "model": "user-model"
                  }
                }
                """);
            Directory.CreateDirectory(Path.Combine(workspaceRoot, ".clawsharp"));
            await File.WriteAllTextAsync(
                ClaudeConfigPaths.GetProjectSettingsFilePath(workspaceRoot),
                """
                {
                  "runtime": {
                    "permissionMode": "not-a-real-mode"
                  }
                }
                """);

            var result = await new SettingsBootstrapper().LoadAsync(workspaceRoot);

            Assert.Single(result.Issues);
            Assert.Equal(ClaudeConfigPaths.GetProjectSettingsFilePath(workspaceRoot), result.Issues[0].File);
            Assert.Equal("$.runtime.permissionMode", result.Issues[0].Path);
            Assert.Equal("user-model", result.Settings.Runtime.Model);
            Assert.Equal(new ClawSharpSettings().Runtime.PermissionMode, result.Settings.Runtime.PermissionMode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", originalConfigDir);

            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }

            if (Directory.Exists(configDir))
            {
                Directory.Delete(configDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LoadAsync_Captures_PerSource_AutoMode_Preferences()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-settings-bootstrap", Guid.NewGuid().ToString("N"));
        var configDir = Path.Combine(Path.GetTempPath(), "clawsharp-settings-config", Guid.NewGuid().ToString("N"));
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
                  "skipAutoPermissionPrompt": true
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
                  "useAutoModeDuringPlan": true
                }
                """);

            var result = await new SettingsBootstrapper().LoadAsync(workspaceRoot);

            Assert.Empty(result.Issues);
            Assert.True(result.SourcePreferences.TryGetValue(PermissionRuleSource.UserSettings, out var userPreferences));
            Assert.True(userPreferences!.SkipAutoPermissionPrompt);
            Assert.True(result.SourcePreferences.TryGetValue(PermissionRuleSource.ProjectSettings, out var projectPreferences));
            Assert.False(projectPreferences!.UseAutoModeDuringPlan);
            Assert.True(result.SourcePreferences.TryGetValue(PermissionRuleSource.LocalSettings, out var localPreferences));
            Assert.True(localPreferences!.UseAutoModeDuringPlan);
            Assert.True(result.Settings.SkipAutoPermissionPrompt);
            Assert.True(result.Settings.UseAutoModeDuringPlan);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", originalConfigDir);

            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }

            if (Directory.Exists(configDir))
            {
                Directory.Delete(configDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LoadAsync_Reads_Sandbox_Settings_With_Ts_Shaped_Defaults_And_Precedence()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-settings-bootstrap", Guid.NewGuid().ToString("N"));
        var configDir = Path.Combine(Path.GetTempPath(), "clawsharp-settings-config", Guid.NewGuid().ToString("N"));
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
                  "sandbox": {
                    "enabled": true,
                    "allowUnsandboxedCommands": true
                  }
                }
                """);
            Directory.CreateDirectory(Path.Combine(workspaceRoot, ".clawsharp"));
            await File.WriteAllTextAsync(
                ClaudeConfigPaths.GetProjectSettingsFilePath(workspaceRoot),
                """
                {
                  "sandbox": {
                    "allowUnsandboxedCommands": false,
                    "failIfUnavailable": true
                  }
                }
                """);

            var result = await new SettingsBootstrapper().LoadAsync(workspaceRoot);

            Assert.Empty(result.Issues);
            Assert.True(result.Settings.Sandbox.Enabled);
            Assert.False(result.Settings.Sandbox.AllowUnsandboxedCommands);
            Assert.True(result.Settings.Sandbox.FailIfUnavailable);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", originalConfigDir);

            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }

            if (Directory.Exists(configDir))
            {
                Directory.Delete(configDir, recursive: true);
            }
        }
    }
}
