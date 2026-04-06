using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Tasks;

namespace ClawSharp.IntegrationTests;

public sealed class PluginSkillHookIntegrationTests
{
    [Fact]
    public async Task ExtensionBootstrapper_Loads_Plugin_Skill_And_Executable_Hook()
    {
        var shell = await ResolveHookShellAsync();
        if (shell is null)
        {
            return;
        }

        var workspaceRoot = CreateTempDirectory("clawsharp-plugin-workspace");
        var configRoot = CreateTempDirectory("clawsharp-plugin-config");
        var pluginsRoot = Path.Combine(configRoot, "plugins");
        Directory.CreateDirectory(pluginsRoot);
        var pluginRoot = Path.Combine(pluginsRoot, "reviewer");
        Directory.CreateDirectory(pluginRoot);
        Directory.CreateDirectory(Path.Combine(pluginRoot, "skills", "reviewer"));
        Directory.CreateDirectory(Path.Combine(pluginRoot, "hooks"));

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(pluginsRoot, "installed_plugins.json"),
                $$"""
                {
                  "version": 2,
                  "plugins": {
                    "reviewer@anthropic-tools": [
                      {
                        "scope": "user",
                        "installPath": "{{pluginRoot.Replace("\\", "\\\\", StringComparison.Ordinal)}}"
                      }
                    ]
                  }
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(pluginRoot, "plugin.json"),
                """
                {
                  "name": "reviewer",
                  "description": "Reviewer plugin",
                  "version": "1.0.0",
                  "skills": ["skills/reviewer"],
                  "hooks": ["hooks/reviewer-hooks.json"]
                }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(pluginRoot, "skills", "reviewer", "SKILL.md"),
                "# Reviewer\n\nPlugin-provided skill.");
            await File.WriteAllTextAsync(
                Path.Combine(pluginRoot, "hooks", "reviewer-hooks.json"),
                $$"""
                {
                  "PreToolUse": [
                    {
                      "matcher": "*",
                      "hooks": [
                        {
                          "type": "command",
                          "command": "{{BuildHookCommand(shell.Value)}}",
                          "shell": "{{GetShellName(shell.Value)}}",
                          "timeout": 5
                        }
                      ]
                    }
                  ]
                }
                """);

            var bootstrapper = new ExtensionBootstrapper(
                userConfigHomeDir: configRoot,
                pluginsDirectoryPath: pluginsRoot);
            var result = await bootstrapper.LoadAsync(
                workspaceRoot,
                new StartupEnvironment(configRoot),
                new ClawSharpSettings());

            var plugin = Assert.Single(result.Plugins);
            var skill = Assert.Single(result.Skills);
            var hook = Assert.Single(result.Hooks);

            Assert.Equal("reviewer@anthropic-tools", plugin.PluginId);
            Assert.True(plugin.Enabled);
            Assert.Equal("reviewer", skill.Name);
            Assert.Equal("plugin", skill.Source);
            Assert.Equal(HookEvent.PreToolUse, hook.Event);
            Assert.Equal(plugin.PluginId, hook.PluginId);
            Assert.Equal(pluginRoot, hook.PluginRoot);

            var executor = new HookExecutor();
            var execution = await executor.ExecuteAsync(
                [hook],
                new HookExecutionRequest(HookEvent.PreToolUse, workspaceRoot, """{"ok":true}"""));

            var trace = Assert.Single(execution.Traces);
            Assert.True(trace.Succeeded, trace.Stderr);
            Assert.Contains(pluginRoot.Replace('\\', '/'), trace.Stdout.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
            Assert.Contains(workspaceRoot.Replace('\\', '/'), trace.Stdout.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(workspaceRoot);
            DeleteDirectory(configRoot);
        }
    }

    private static async Task<HookShell?> ResolveHookShellAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            var powerShellPath = await PowerShellDetection.GetCachedPowerShellPathAsync();
            if (!string.IsNullOrWhiteSpace(powerShellPath))
            {
                return HookShell.PowerShell;
            }
        }

        var bashPath = await BashShellDetection.FindSuitableShellAsync();
        return string.IsNullOrWhiteSpace(bashPath) ? null : HookShell.Bash;
    }

    private static string BuildHookCommand(HookShell shell)
    {
        return shell == HookShell.PowerShell
            ? "Write-Output $env:CLAUDE_PLUGIN_ROOT; Write-Output (Get-Location).Path"
            : "printf '%s\\\\n' \\\"$CLAUDE_PLUGIN_ROOT\\\" \\\"$PWD\\\"";
    }

    private static string GetShellName(HookShell shell)
    {
        return shell == HookShell.PowerShell ? "powershell" : "bash";
    }

    private static string CreateTempDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
