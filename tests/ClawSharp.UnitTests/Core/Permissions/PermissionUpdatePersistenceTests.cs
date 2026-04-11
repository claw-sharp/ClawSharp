using System.Text.Json;
using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class PermissionUpdatePersistenceTests
{
    [Fact]
    public void ApplyPermissionUpdate_AddRules_Appends_Rules_To_Context_Source()
    {
        var context = ToolPermissionContexts.CreateEmpty() with
        {
            AlwaysAllowRules = new Dictionary<PermissionRuleSource, IReadOnlyList<string>>
            {
                [PermissionRuleSource.Session] = ["Read"]
            }
        };

        var updated = PermissionUpdateApplier.ApplyPermissionUpdate(
            context,
            new AddPermissionRulesUpdate(
                PermissionUpdateDestination.Session,
                [new PermissionRuleValue("Bash", "echo:*")],
                PermissionBehavior.Allow));

        Assert.Equal(["Read", "Bash(echo:*)"], updated.AlwaysAllowRules[PermissionRuleSource.Session]);
    }

    [Fact]
    public void ApplyPermissionUpdate_RemoveRules_Removes_Exact_String_Matches()
    {
        var context = ToolPermissionContexts.CreateEmpty() with
        {
            AlwaysDenyRules = new Dictionary<PermissionRuleSource, IReadOnlyList<string>>
            {
                [PermissionRuleSource.CliArg] = ["Bash(rm:*)", "Bash(curl:*)"]
            }
        };

        var updated = PermissionUpdateApplier.ApplyPermissionUpdate(
            context,
            new RemovePermissionRulesUpdate(
                PermissionUpdateDestination.CliArg,
                [new PermissionRuleValue("Bash", "rm:*")],
                PermissionBehavior.Deny));

        Assert.Equal(["Bash(curl:*)"], updated.AlwaysDenyRules[PermissionRuleSource.CliArg]);
    }

    [Fact]
    public void ApplyPermissionUpdate_AddDirectories_Overwrites_Source_By_Path()
    {
        var context = ToolPermissionContexts.CreateEmpty() with
        {
            AdditionalWorkingDirectories = new Dictionary<string, AdditionalWorkingDirectory>(StringComparer.Ordinal)
            {
                ["src"] = new("src", PermissionRuleSource.Session)
            }
        };

        var updated = PermissionUpdateApplier.ApplyPermissionUpdate(
            context,
            new AddPermissionDirectoriesUpdate(
                PermissionUpdateDestination.ProjectSettings,
                ["src", "tests"]));

        Assert.Equal(PermissionRuleSource.ProjectSettings, updated.AdditionalWorkingDirectories["src"].Source);
        Assert.Equal(PermissionRuleSource.ProjectSettings, updated.AdditionalWorkingDirectories["tests"].Source);
    }

    [Fact]
    public void PersistPermissionUpdate_AddRules_Preserves_Unrecognized_Keys_And_Deduplicates_Normalized_Rules()
    {
        var workspaceRoot = CreateWorkspace();
        var configDir = CreateConfigDir();
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", configDir);

        try
        {
            var userSettingsPath = ClaudeConfigPaths.GetUserSettingsFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(userSettingsPath)!);
            File.WriteAllText(
                userSettingsPath,
                """
                {
                  "runtime": {
                    "permissionMode": "plan"
                  },
                  "hooks": {
                    "unknownShape": 1
                  },
                  "permissions": {
                    "allow": ["Bash(*)"]
                  }
                }
                """);

            var service = new PermissionUpdatePersistenceService();
            var persisted = service.PersistPermissionUpdate(
                workspaceRoot,
                new ClawSharpSettings(),
                new AddPermissionRulesUpdate(
                    PermissionUpdateDestination.UserSettings,
                    [new PermissionRuleValue("Bash")],
                    PermissionBehavior.Allow));

            Assert.True(persisted);

            using var document = JsonDocument.Parse(File.ReadAllText(userSettingsPath));
            var root = document.RootElement;
            Assert.Equal("plan", root.GetProperty("runtime").GetProperty("permissionMode").GetString());
            Assert.Equal(1, root.GetProperty("hooks").GetProperty("unknownShape").GetInt32());
            Assert.Equal(1, root.GetProperty("permissions").GetProperty("allow").GetArrayLength());
            Assert.Equal("Bash(*)", root.GetProperty("permissions").GetProperty("allow")[0].GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            DeleteDirectoryIfExists(workspaceRoot);
            DeleteDirectoryIfExists(configDir);
        }
    }

    [Fact]
    public void PersistPermissionUpdate_RemoveRules_Uses_Normalized_Comparison()
    {
        var workspaceRoot = CreateWorkspace();
        var configDir = CreateConfigDir();
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", configDir);

        try
        {
            Directory.CreateDirectory(Path.Combine(workspaceRoot, ".clawsharp"));
            var projectSettingsPath = ClaudeConfigPaths.GetProjectSettingsFilePath(workspaceRoot);
            File.WriteAllText(
                projectSettingsPath,
                """
                {
                  "permissions": {
                    "allow": ["Task"]
                  }
                }
                """);

            var service = new PermissionUpdatePersistenceService();
            var persisted = service.PersistPermissionUpdate(
                workspaceRoot,
                new ClawSharpSettings(),
                new RemovePermissionRulesUpdate(
                    PermissionUpdateDestination.ProjectSettings,
                    [new PermissionRuleValue("Agent")],
                    PermissionBehavior.Allow));

            Assert.True(persisted);

            using var document = JsonDocument.Parse(File.ReadAllText(projectSettingsPath));
            Assert.Equal(0, document.RootElement.GetProperty("permissions").GetProperty("allow").GetArrayLength());
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            DeleteDirectoryIfExists(workspaceRoot);
            DeleteDirectoryIfExists(configDir);
        }
    }

    [Fact]
    public void PersistPermissionUpdate_SetMode_And_Directories_Writes_Permissions_Block()
    {
        var workspaceRoot = CreateWorkspace();
        var configDir = CreateConfigDir();
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", configDir);

        try
        {
            Directory.CreateDirectory(Path.Combine(workspaceRoot, ".clawsharp"));
            var localSettingsPath = ClaudeConfigPaths.GetLocalSettingsFilePath(workspaceRoot);
            var service = new PermissionUpdatePersistenceService();

            service.PersistPermissionUpdates(
                workspaceRoot,
                new ClawSharpSettings(),
                [
                    new SetPermissionModeUpdate(PermissionUpdateDestination.LocalSettings, PermissionMode.AcceptEdits),
                    new AddPermissionDirectoriesUpdate(PermissionUpdateDestination.LocalSettings, ["src", "tests"]),
                    new RemovePermissionDirectoriesUpdate(PermissionUpdateDestination.LocalSettings, ["src"])
                ]);

            using var document = JsonDocument.Parse(File.ReadAllText(localSettingsPath));
            var permissions = document.RootElement.GetProperty("permissions");
            Assert.Equal("acceptEdits", permissions.GetProperty("defaultMode").GetString());
            Assert.Equal(1, permissions.GetProperty("additionalDirectories").GetArrayLength());
            Assert.Equal("tests", permissions.GetProperty("additionalDirectories")[0].GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            DeleteDirectoryIfExists(workspaceRoot);
            DeleteDirectoryIfExists(configDir);
        }
    }

    [Fact]
    public void PersistPermissionUpdate_AddRules_Respects_Managed_Only_Gate()
    {
        var workspaceRoot = CreateWorkspace();
        var configDir = CreateConfigDir();
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", configDir);

        try
        {
            var service = new PermissionUpdatePersistenceService();
            var persisted = service.PersistPermissionUpdate(
                workspaceRoot,
                new ClawSharpSettings
                {
                    AllowManagedPermissionRulesOnly = true
                },
                new AddPermissionRulesUpdate(
                    PermissionUpdateDestination.UserSettings,
                    [new PermissionRuleValue("Read", "/tmp/**")],
                    PermissionBehavior.Allow));

            Assert.False(persisted);
            Assert.False(File.Exists(ClaudeConfigPaths.GetUserSettingsFilePath()));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            DeleteDirectoryIfExists(workspaceRoot);
            DeleteDirectoryIfExists(configDir);
        }
    }

    [Fact]
    public async Task JsonApprovalRequestStore_Saves_And_Loads_String_Enum_Decisions()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-approval-store", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var storePath = Path.Combine(tempDir, "approval-requests.json");
        var store = new JsonApprovalRequestStore(storePath);
        var createdAt = DateTimeOffset.Parse("2026-04-02T12:00:00+00:00");

        try
        {
            store.Save(
                [
                    new ApprovalRequest("req-1", "Run outside of the sandbox", ApprovalDecision.Approved, createdAt)
                ]);

            var raw = File.ReadAllText(storePath);
            Assert.Contains("\"decision\": \"approved\"", raw, StringComparison.Ordinal);

            var loaded = await store.LoadAsync();
            Assert.Single(loaded);
            Assert.Equal(ApprovalDecision.Approved, loaded[0].Decision);
            Assert.Equal(createdAt, loaded[0].CreatedAt);
        }
        finally
        {
            DeleteDirectoryIfExists(tempDir);
        }
    }

    private static string CreateWorkspace()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-permission-updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        return workspaceRoot;
    }

    private static string CreateConfigDir()
    {
        var configDir = Path.Combine(Path.GetTempPath(), "clawsharp-config", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(configDir);
        return configDir;
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
