// TS origin: ./components/design-system/LoadingState.tsx, ./components/shell/ShellProgressMessage.tsx, ./components/messages/HookProgressMessage.tsx, ./components/Spinner.tsx
using System.Text.Json.Nodes;
using ClawSharp.Ui.Terminal;

namespace ClawSharp.UnitTests;

public sealed class TerminalProgressIndicatorRendererTests
{
    private readonly TerminalProgressIndicatorRenderer _renderer = new();

    [Fact]
    public void TryRender_McpProgress_Uses_ProgressMessage_And_Counts()
    {
        var progress = new JsonObject
        {
            ["type"] = "mcp_progress",
            ["status"] = "progress",
            ["serverName"] = "filesystem",
            ["toolName"] = "read_resource",
            ["progressMessage"] = "Loading resources...",
            ["progress"] = 2,
            ["total"] = 5
        };

        var rendered = _renderer.TryRender(progress);

        Assert.Equal("  Loading resources... (2/5)", rendered);
    }

    [Fact]
    public void TryRender_McpProgress_Falls_Back_To_Default_Headline()
    {
        var progress = new JsonObject
        {
            ["type"] = "mcp_progress",
            ["status"] = "started",
            ["serverName"] = "filesystem",
            ["toolName"] = "read_resource"
        };

        var rendered = _renderer.TryRender(progress);

        Assert.Equal("  Running read_resource via filesystem...", rendered);
    }

    [Fact]
    public void TryRender_WaitingForTask_Matches_Current_Task_Waiting_Text()
    {
        var progress = new JsonObject
        {
            ["type"] = "waiting_for_task",
            ["taskDescription"] = "Run tests in background"
        };

        var rendered = _renderer.TryRender(progress);

        Assert.Equal(
            "  Run tests in background" + Environment.NewLine +
            "     Waiting for task (esc to give additional instructions)",
            rendered);
    }

    [Fact]
    public void TryRender_McpCompleted_Returns_Null()
    {
        var progress = new JsonObject
        {
            ["type"] = "mcp_progress",
            ["status"] = "completed",
            ["serverName"] = "filesystem",
            ["toolName"] = "read_resource"
        };

        var rendered = _renderer.TryRender(progress);

        Assert.Null(rendered);
    }
}
