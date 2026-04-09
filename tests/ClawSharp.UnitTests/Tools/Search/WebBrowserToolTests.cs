using ClawSharp.Core;
using ClawSharp.Tasks;
using ClawSharp.Tools;

namespace ClawSharp.UnitTests;

public sealed class WebBrowserToolTests
{
    [Fact]
    public async Task WebBrowserTool_ValidateAsync_Allows_Existing_FileUrl()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-webbrowser-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var htmlPath = Path.Combine(tempDir, "snake.html");
            await File.WriteAllTextAsync(htmlPath, "<!doctype html><title>Snake</title>");

            var tool = CreateWebBrowserTool();
            var validation = await tool.ValidateAsync(CreateContext(
                tempDir,
                $$"""{"action":"open","url":"{{new Uri(htmlPath).AbsoluteUri}}"}"""));

            Assert.True(validation.IsValid, validation.ErrorMessage);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task WebBrowserTool_ValidateAsync_Rejects_Missing_FileUrl()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-webbrowser-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var missingPath = Path.Combine(tempDir, "missing.html");

            var tool = CreateWebBrowserTool();
            var validation = await tool.ValidateAsync(CreateContext(
                tempDir,
                $$"""{"action":"open","url":"{{new Uri(missingPath).AbsoluteUri}}"}"""));

            Assert.False(validation.IsValid);
            Assert.Contains("Local file does not exist", validation.ErrorMessage, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static IClawSharpTool CreateWebBrowserTool()
    {
        var toolType = typeof(ToolRegistry).Assembly.GetType("ClawSharp.Tools.WebBrowserTool", throwOnError: true)!;
        return (IClawSharpTool)Activator.CreateInstance(toolType, nonPublic: true)!;
    }

    private static ToolExecutionContext CreateContext(string workspaceRoot, string arguments)
    {
        return new ToolExecutionContext(
            arguments,
            workspaceRoot,
            new DefaultSessionFactory(workspaceRoot).Create(),
            new NullClawSharpAppStateStore(workspaceRoot),
            new TaskRegistry(workspaceRoot),
            new TaskRegistry(workspaceRoot),
            new ClawSharpSettings(),
            FileStateCache.CreateWithSizeLimit(FileStateCache.DefaultMaxEntries),
            ToolPermissionContexts.CreateEmpty(),
            BuiltInAgentDefinitions.GetBuiltInAgents(),
            new NullFileUpdateNotifier(),
            new NullPermissionPrompter());
    }
}
