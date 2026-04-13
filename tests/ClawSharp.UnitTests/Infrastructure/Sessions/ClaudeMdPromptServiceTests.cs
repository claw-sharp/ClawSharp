using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public sealed class ClaudeMdPromptServiceTests
{
    [Fact]
    public async Task LoadPromptAsync_LoadsAncestorAndWorkspaceInstructionsInPriorityOrder()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "clawsharp-claudemd-root", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(rootDirectory, "workspace");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(Path.Combine(workspaceRoot, ".claude", "rules"));

        await File.WriteAllTextAsync(Path.Combine(rootDirectory, "CLAUDE.md"), "Parent instructions");
        await File.WriteAllTextAsync(Path.Combine(workspaceRoot, ".claude", "CLAUDE.md"), "Workspace project instructions");
        await File.WriteAllTextAsync(Path.Combine(workspaceRoot, ".claude", "rules", "always.md"), "Always apply this rule");
        await File.WriteAllTextAsync(
            Path.Combine(workspaceRoot, ".claude", "rules", "scoped.md"),
            """
            ---
            paths:
              - src/**/*.cs
            ---
            Only for specific files
            """);
        await File.WriteAllTextAsync(
            Path.Combine(workspaceRoot, "CLAUDE.local.md"),
            """
            ---
            owner: local
            ---
            Local instructions
            """);

        try
        {
            var service = new ClaudeMdPromptService(
                searchPathResolver: (root, _) => Task.FromResult(new WorkspaceSearchPaths(root, null, [workspaceRoot, rootDirectory])));

            var result = await service.LoadPromptAsync(workspaceRoot);

            Assert.NotNull(result);
            Assert.Contains("Codebase and user instructions are shown below.", result, StringComparison.Ordinal);
            Assert.Contains("Parent instructions", result, StringComparison.Ordinal);
            Assert.Contains("Workspace project instructions", result, StringComparison.Ordinal);
            Assert.Contains("Always apply this rule", result, StringComparison.Ordinal);
            Assert.Contains("Local instructions", result, StringComparison.Ordinal);
            Assert.DoesNotContain("Only for specific files", result, StringComparison.Ordinal);
            Assert.DoesNotContain("paths:", result, StringComparison.Ordinal);
            Assert.DoesNotContain("owner: local", result, StringComparison.Ordinal);

            Assert.True(
                result.IndexOf("Parent instructions", StringComparison.Ordinal) <
                result.IndexOf("Workspace project instructions", StringComparison.Ordinal));
            Assert.True(
                result.IndexOf("Workspace project instructions", StringComparison.Ordinal) <
                result.IndexOf("Always apply this rule", StringComparison.Ordinal));
            Assert.True(
                result.IndexOf("Always apply this rule", StringComparison.Ordinal) <
                result.IndexOf("Local instructions", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
            {
                Directory.Delete(rootDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LoadPromptAsync_NoInstructionFiles_ReturnsNull()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-claudemd-empty", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var service = new ClaudeMdPromptService(
                searchPathResolver: (root, _) => Task.FromResult(new WorkspaceSearchPaths(root, null, [workspaceRoot])));

            var result = await service.LoadPromptAsync(workspaceRoot);

            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ReplMainThreadContextProvider_IncludesClaudeMdSectionAfterDynamicBoundary()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-claudemd-provider", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        await File.WriteAllTextAsync(Path.Combine(workspaceRoot, "CLAUDE.md"), "Provider instructions");

        try
        {
            var promptService = new ClaudeMdPromptService(
                searchPathResolver: (root, _) => Task.FromResult(new WorkspaceSearchPaths(root, null, [workspaceRoot])));
            var provider = new ReplMainThreadTurnContextProvider(
                workspaceRoot,
                new ClawSharpSettings(),
                claudeMdPromptService: promptService);

            var context = await provider.GetReplMainThreadContextAsync();

            var boundaryIndex = context.SystemPrompt
                .Select((section, index) => new { section, index })
                .First(item => string.Equals(item.section, QueryRequestBuilder.SystemPromptDynamicBoundary, StringComparison.Ordinal))
                .index;
            var claudeIndex = context.SystemPrompt
                .Select((section, index) => new { section, index })
                .First(item => item.section.Contains("Provider instructions", StringComparison.Ordinal))
                .index;

            Assert.True(boundaryIndex >= 0);
            Assert.True(claudeIndex > boundaryIndex);
        }
        finally
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }
}
