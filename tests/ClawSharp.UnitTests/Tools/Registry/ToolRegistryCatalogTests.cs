// TS parity status: locks the current C# port of built-in-first catalog assembly and blanket tool deny-rule filtering; full 1:1 parity still depends on the missing TS-only ToolSearch/deferred-discovery and REPL-mode tool surfaces.
using ClawSharp.Core;
using ClawSharp.Tasks;
using ClawSharp.Tools;

namespace ClawSharp.UnitTests;

public sealed class ToolRegistryCatalogTests
{
    [Fact]
    public void All_Keeps_BuiltIns_Before_Dynamic_Tools_And_Sorts_Each_Partition()
    {
        var registry = new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry());
        registry.Register(new TestTool("ZuluTool"));
        registry.Register(new TestTool("AlphaTool"));

        var names = registry.All.Select(static tool => tool.Name).ToArray();

        Assert.Equal(["Agent", "Bash", "Edit"], names.Take(3));
        Assert.Equal(["AlphaTool", "ZuluTool"], names.TakeLast(2));
    }

    [Fact]
    public void All_Filters_BuiltIn_Tools_Matched_By_Blanket_Deny_Rules()
    {
        var registry = new ToolRegistry(
            Environment.CurrentDirectory,
            new TaskRegistry(),
            toolPermissionContext: CreatePermissionContext(denyRules: ["Bash", "Task"]));

        var names = registry.All.Select(static tool => tool.Name).ToArray();

        Assert.DoesNotContain("Bash", names);
        Assert.DoesNotContain("Agent", names);
    }

    [Fact]
    public void All_Filters_Mcp_Tools_By_Server_Level_Deny_Rule()
    {
        var registry = new ToolRegistry(
            Environment.CurrentDirectory,
            new TaskRegistry(),
            toolPermissionContext: CreatePermissionContext(denyRules: ["mcp__docs"]));
        registry.Register(new TestTool("mcp__docs__search"));
        registry.Register(new TestTool("mcp__docs__read"));
        registry.Register(new TestTool("mcp__other__search"));

        var names = registry.All.Select(static tool => tool.Name).ToArray();

        Assert.DoesNotContain("mcp__docs__search", names);
        Assert.DoesNotContain("mcp__docs__read", names);
        Assert.Contains("mcp__other__search", names);
    }

    [Fact]
    public void All_Does_Not_Filter_Tools_For_Command_Scoped_Deny_Rules()
    {
        var registry = new ToolRegistry(
            Environment.CurrentDirectory,
            new TaskRegistry(),
            toolPermissionContext: CreatePermissionContext(denyRules: ["Bash(git status)"]));

        Assert.Contains(registry.All, static tool => tool.Name == "Bash");
    }

    [Fact]
    public void RegisterOrReplace_Replaces_Dynamic_Tool_Without_Disturbing_BuiltIn_Name_Preference()
    {
        var registry = new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry());
        registry.Register(new TestTool("SearchShadow", "first"));
        registry.RegisterOrReplace(new TestTool("SearchShadow", "second"));
        registry.Register(new TestTool("Write", "shadow write"));

        Assert.Equal("second", Assert.Single(registry.All, static tool => tool.Name == "SearchShadow").Description);
        Assert.Equal(
            "Write new files",
            Assert.Single(registry.All, static tool => tool.Name == "Write").Description);
        Assert.True(registry.TryResolve("Write", out var resolved));
        Assert.Equal("Write new files", resolved?.Descriptor.Description);
    }

    private static ToolPermissionContext CreatePermissionContext(IReadOnlyList<string>? denyRules = null)
    {
        return new ToolPermissionContext(
            PermissionMode.Default,
            new Dictionary<string, AdditionalWorkingDirectory>(StringComparer.Ordinal),
            new Dictionary<PermissionRuleSource, IReadOnlyList<string>>(),
            new Dictionary<PermissionRuleSource, IReadOnlyList<string>>
            {
                [PermissionRuleSource.Session] = denyRules ?? []
            },
            new Dictionary<PermissionRuleSource, IReadOnlyList<string>>(),
            IsBypassPermissionsModeAvailable: false);
    }

    private sealed class TestTool : IClawSharpTool
    {
        public TestTool(string name, string? description = null)
        {
            Descriptor = new ToolDescriptor(name, description ?? name);
        }

        public ToolDescriptor Descriptor { get; }

        public bool IsEnabled() => true;

        public bool IsConcurrencySafe(string arguments) => true;

        public bool IsReadOnly(string arguments) => true;

        public string? RenderToolUseProgressMessage(IReadOnlyList<ToolProgressUpdate> progressMessages) => null;

        public string? RenderToolResultMessage(string content, System.Text.Json.Nodes.JsonNode? structuredOutput, IReadOnlyList<ToolProgressUpdate> progressMessages) => content;

        public Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ToolValidationResult.Valid());
        }

        public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ToolExecutionResult(true, "ok"));
        }
    }
}
