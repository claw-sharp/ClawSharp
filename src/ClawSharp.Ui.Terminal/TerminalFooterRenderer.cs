// TS origin: ./components/StatusLine.tsx, ./components/PromptInput/PromptInputFooterLeftSide.tsx
using ClawSharp.Core;

namespace ClawSharp.Ui.Terminal;

public sealed class TerminalFooterRenderer
{
    private readonly BackgroundTaskSummaryRenderer _backgroundTaskSummaryRenderer = new();

    public IReadOnlyList<string> Render(ClawSharpAppState appState)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(appState.StatusLineText))
        {
            lines.Add(appState.StatusLineText.Trim());
        }

        var parts = new List<string>();
        var modePart = RenderModePart(appState.ToolPermissionContext.Mode);
        if (!string.IsNullOrWhiteSpace(modePart))
        {
            parts.Add(modePart);
        }

        var runningTaskSummary = _backgroundTaskSummaryRenderer.Render(
            ClawSharpAppStateSelectors.GetRunningTasks(appState));
        if (!string.IsNullOrWhiteSpace(runningTaskSummary))
        {
            parts.Add(runningTaskSummary);
        }

        var activeSessionDisplayName = ClawSharpAppStateSelectors.GetActiveSessionDisplayName(appState);
        if (!string.IsNullOrWhiteSpace(activeSessionDisplayName))
        {
            parts.Add($"session {activeSessionDisplayName}");
        }

        var mainLoopModel = MainLoopModelResolver.Resolve(
            appState.MainLoopModel,
            appState.Settings.Runtime.Model);
        if (!string.IsNullOrWhiteSpace(mainLoopModel))
        {
            parts.Add($"model {MainLoopModelResolver.RenderSetting(mainLoopModel)}");
        }

        if (parts.Count > 0)
        {
            lines.Add(string.Join(" · ", parts));
        }

        return lines;
    }

    private static string? RenderModePart(PermissionMode mode)
    {
        return mode switch
        {
            PermissionMode.Default => null,
            PermissionMode.Plan => "plan mode on",
            PermissionMode.AcceptEdits => "accept edits on",
            PermissionMode.BypassPermissions => "bypass permissions on",
            PermissionMode.DontAsk => "don't ask on",
            PermissionMode.Auto => "auto mode on",
            PermissionMode.Bubble => "bubble mode on",
            _ => null
        };
    }
}
