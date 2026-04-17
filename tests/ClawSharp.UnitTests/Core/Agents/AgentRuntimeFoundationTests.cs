using System.Text;
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Tasks;
using ClawSharp.Tools;

namespace ClawSharp.UnitTests;

public sealed class AgentRuntimeFoundationTests
{
    [Fact]
    public async Task AgentBootstrapper_LoadAsync_Loads_BuiltIn_And_Markdown_Agents_With_Ts_Source_Precedence()
    {
        var tempRoot = CreateTempDirectory();
        var workspaceRoot = Path.Combine(tempRoot, "repo", "child");
        var managedRoot = Path.Combine(tempRoot, "managed");
        var userConfigHomeDir = Path.Combine(tempRoot, "user");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(Path.Combine(tempRoot, "repo", ".git"));

        CreateAgent(
            Path.Combine(managedRoot, ".clawsharp", "agents"),
            "general-purpose",
            "policy override",
            """
            tools:
              - Read
              - Bash
            permissionMode: plan
            background: true
            """);
        CreateAgent(
            Path.Combine(userConfigHomeDir, "agents"),
            "user-agent",
            "user description");
        CreateAgent(
            Path.Combine(workspaceRoot, ".clawsharp", "agents"),
            "workspace-agent",
            "workspace description");

        var bootstrapper = new AgentBootstrapper(managedFilePath: managedRoot, userConfigHomeDir: userConfigHomeDir);
        var result = await bootstrapper.LoadAsync(
            workspaceRoot,
            new StartupEnvironment(userConfigHomeDir, BareMode: false, DisablePolicySkills: false));

        Assert.Contains(result.AllAgents, static agent => agent.AgentType == "statusline-setup");
        Assert.Contains(result.AllAgents, static agent => agent.AgentType == "user-agent");
        Assert.Contains(result.AllAgents, static agent => agent.AgentType == "workspace-agent");

        var activeGeneralPurpose = Assert.Single(result.ActiveAgents, static agent => agent.AgentType == "general-purpose");
        Assert.Equal("policySettings", activeGeneralPurpose.Source);
        Assert.Equal(["Read", "Bash"], activeGeneralPurpose.Tools);
        Assert.Equal(PermissionMode.Plan, activeGeneralPurpose.PermissionMode);
        Assert.True(activeGeneralPurpose.Background);
    }

    [Fact]
    public async Task AgentBootstrapper_LoadAsync_Returns_BuiltIns_Only_In_Bare_Mode()
    {
        var tempRoot = CreateTempDirectory();
        var workspaceRoot = Path.Combine(tempRoot, "repo");
        var managedRoot = Path.Combine(tempRoot, "managed");
        var userConfigHomeDir = Path.Combine(tempRoot, "user");
        Directory.CreateDirectory(workspaceRoot);

        CreateAgent(Path.Combine(managedRoot, ".clawsharp", "agents"), "managed-agent", "managed");
        CreateAgent(Path.Combine(userConfigHomeDir, "agents"), "user-agent", "user");
        CreateAgent(Path.Combine(workspaceRoot, ".clawsharp", "agents"), "project-agent", "project");

        var bootstrapper = new AgentBootstrapper(managedFilePath: managedRoot, userConfigHomeDir: userConfigHomeDir);
        var result = await bootstrapper.LoadAsync(
            workspaceRoot,
            new StartupEnvironment(userConfigHomeDir, BareMode: true, DisablePolicySkills: false));

        Assert.All(result.ActiveAgents, static agent => Assert.Equal("built-in", agent.Source));
        Assert.DoesNotContain(result.ActiveAgents, static agent => agent.AgentType == "managed-agent");
    }

    [Fact]
    public async Task AgentBootstrapper_LoadAsync_Logs_ProjectDirectory_Substeps()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var originalDebug = Environment.GetEnvironmentVariable("DEBUG");
        var tempRoot = CreateTempDirectory();
        var configDir = Path.Combine(tempRoot, "config");
        var workspaceRoot = Path.Combine(tempRoot, "repo", "child");
        var managedRoot = Path.Combine(tempRoot, "managed");
        var userConfigHomeDir = Path.Combine(tempRoot, "user");
        Directory.CreateDirectory(configDir);
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(Path.Combine(tempRoot, "repo", ".git"));

        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", configDir);
        Environment.SetEnvironmentVariable("DEBUG", "1");
        ClawSharpTelemetry.ResetForTesting();

        try
        {
            ClawSharpTelemetry.Initialize(configDir, "session-agent-bootstrap");
            CreateAgent(Path.Combine(managedRoot, ".clawsharp", "agents"), "managed-agent", "managed");
            CreateAgent(Path.Combine(userConfigHomeDir, "agents"), "user-agent", "user");
            CreateAgent(Path.Combine(workspaceRoot, ".clawsharp", "agents"), "project-agent", "project");

            var bootstrapper = new AgentBootstrapper(
                managedFilePath: managedRoot,
                userConfigHomeDir: userConfigHomeDir,
                canonicalGitRootResolver: (_, _) => Task.FromResult<string?>(Path.Combine(tempRoot, "repo")));

            _ = await bootstrapper.LoadAsync(
                workspaceRoot,
                new StartupEnvironment(userConfigHomeDir, BareMode: false, DisablePolicySkills: false));

            var debugLog = ReadAllTextShared(ClawSharpTelemetry.GetDebugLogPath());
            Assert.Contains("[AgentBootstrapper:project-directories] start", debugLog, StringComparison.Ordinal);
            Assert.Contains("[AgentBootstrapper:project-directories] complete", debugLog, StringComparison.Ordinal);
            Assert.Contains("gitRoot=", debugLog, StringComparison.Ordinal);
            Assert.Contains("source=projectSettings", debugLog, StringComparison.Ordinal);
        }
        finally
        {
            ClawSharpTelemetry.ResetForTesting();
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            Environment.SetEnvironmentVariable("DEBUG", originalDebug);
            DeleteDirectoryWithRetry(tempRoot);
        }
    }

    [Fact]
    public void ToolRegistry_Falls_Back_To_BuiltIn_Agents_When_Live_State_Is_Empty()
    {
        var workspaceRoot = Environment.CurrentDirectory;
        var appStateStore = new ClawSharpAppStateStore(
            ClawSharpAppState.CreateDefault(
                workspaceRoot,
                StartupEnvironment.Capture(),
                new ClawSharpSettings(),
                [],
                [],
                [],
                [],
                [],
                []));
        var registry = new ToolRegistry(workspaceRoot, new TaskRegistry(workspaceRoot), appStateStore: appStateStore);

        Assert.Contains(registry.AgentDefinitions, static agent => agent.AgentType == "Explore");
    }

    [Fact]
    public async Task AgentTool_Publishes_Ts_Shaped_Metadata_And_Validates_Subagent_Type()
    {
        var registry = new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry());
        Assert.True(registry.TryResolve("Agent", out var agentTool));
        Assert.True(registry.TryResolve("Task", out var legacyAlias));
        Assert.Same(agentTool, legacyAlias);

        var descriptor = Assert.Single(registry.All, static tool => tool.Name == "Agent");
        Assert.True(descriptor.Strict);
        Assert.Equal("delegate work to a subagent", descriptor.SearchHint);
        Assert.Equal("object", descriptor.InputSchema?["type"]?.GetValue<string>());
        Assert.NotNull(descriptor.OutputSchema?["oneOf"]);
        Assert.NotNull(descriptor.InputSchema?["properties"]?["name"]);
        Assert.NotNull(descriptor.InputSchema?["properties"]?["team_name"]);
        Assert.NotNull(descriptor.InputSchema?["properties"]?["mode"]);
        Assert.NotNull(descriptor.InputSchema?["properties"]?["isolation"]);
        Assert.NotNull(descriptor.InputSchema?["properties"]?["cwd"]);

        var session = new DefaultSessionFactory(Environment.CurrentDirectory).Create();
        var settings = new ClawSharpSettings();

        var invalidResult = await registry.ExecuteAsync(
            "Agent",
            """{"description":"test task","prompt":"do it","subagent_type":"missing-agent"}""",
            session,
            settings);
        Assert.False(invalidResult.Success);
        Assert.Contains("Unknown subagent_type 'missing-agent'.", invalidResult.Output);

        var blockedResult = await registry.ExecuteAsync(
            "Agent",
            """{"description":"test task","prompt":"do it"}""",
            session,
            settings);
        Assert.False(blockedResult.Success);
        Assert.Contains("not been wired", blockedResult.Output);
    }

    [Fact]
    public async Task AgentTool_Rejects_MultiAgent_And_Cwd_Isolation_Combinations()
    {
        var registry = new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry());
        var session = new DefaultSessionFactory(Environment.CurrentDirectory).Create();
        var settings = new ClawSharpSettings();

        var multiAgentResult = await registry.ExecuteAsync(
            "Agent",
            """{"description":"test task","prompt":"do it","name":"worker-1"}""",
            session,
            settings);
        Assert.False(multiAgentResult.Success);
        Assert.Contains("multi-agent coordination", multiAgentResult.Output);

        var testCwd = OperatingSystem.IsWindows() ? @"C:\repo" : "/repo";
        var encodedTestCwd = testCwd.Replace(@"\", @"\\");

        var cwdConflictResult = await registry.ExecuteAsync(
            "Agent",
            $$"""{"description":"test task","prompt":"do it","cwd":"{{encodedTestCwd}}","isolation":"worktree"}""",
            session,
            settings);
        Assert.False(cwdConflictResult.Success);
        Assert.Contains("mutually exclusive", cwdConflictResult.Output);

        var cwdResult = await registry.ExecuteAsync(
            "Agent",
            $$"""{"description":"test task","prompt":"do it","cwd":"{{encodedTestCwd}}"}""",
            session,
            settings);
        Assert.False(cwdResult.Success);
        Assert.Contains("cwd", cwdResult.Output, StringComparison.OrdinalIgnoreCase);

        var worktreeResult = await registry.ExecuteAsync(
            "Agent",
            """{"description":"test task","prompt":"do it","isolation":"worktree"}""",
            session,
            settings);
        Assert.False(worktreeResult.Success);
        Assert.Contains("worktree", worktreeResult.Output, StringComparison.OrdinalIgnoreCase);
    }

    private static void CreateAgent(
        string agentsDirectory,
        string name,
        string description,
        string? extraFrontmatter = null)
    {
        Directory.CreateDirectory(agentsDirectory);
        var filePath = Path.Combine(agentsDirectory, $"{name}.md");
        var frontmatter = string.IsNullOrWhiteSpace(extraFrontmatter)
            ? string.Empty
            : extraFrontmatter.Trim() + Environment.NewLine;
        File.WriteAllText(
            filePath,
            $$"""
            ---
            name: {{name}}
            description: {{description}}
            {{frontmatter}}---
            System prompt for {{name}}
            """,
            Encoding.UTF8);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "clawsharp-agent-runtime-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ReadAllTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void DeleteDirectoryWithRetry(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        IOException? lastError = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException exception)
            {
                lastError = exception;
                Thread.Sleep(100);
            }
        }

        if (lastError is not null)
        {
            throw lastError;
        }
    }
}
