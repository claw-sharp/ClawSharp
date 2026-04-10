using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.Ui.Terminal;

public sealed class TerminalFooterRenderer
{
    private readonly BackgroundTaskSummaryRenderer _backgroundTaskSummaryRenderer = new();
    private readonly IQueryCompactionTokenEstimator _tokenEstimator = new ApproximateQueryCompactionTokenEstimator();

    public IReadOnlyList<string> Render(ClawSharpAppState appState, ConversationSession? session = null)
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

        var contextLine = RenderContextLine(appState, session, mainLoopModel);
        if (!string.IsNullOrWhiteSpace(contextLine))
        {
            lines.Add(contextLine);
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

    private string? RenderContextLine(
        ClawSharpAppState appState,
        ConversationSession? session,
        string mainLoopModel)
    {
        if (session is null || session.Messages.Count == 0 || string.IsNullOrWhiteSpace(mainLoopModel))
        {
            return null;
        }

        var activeMessages = QueryCompactBoundaryHelpers.GetMessagesAfterCompactBoundary(session.Messages);
        if (activeMessages.Count == 0)
        {
            return null;
        }

        var estimatedTokens = _tokenEstimator.Estimate(activeMessages);
        var effectiveContextWindow = QueryAutoCompactRunner.GetEffectiveContextWindowSize(mainLoopModel);
        if (effectiveContextWindow <= 0)
        {
            return null;
        }

        var usedPercent = Math.Clamp(
            (int)Math.Round((estimatedTokens / (double)effectiveContextWindow) * 100d),
            0,
            100);

        var compactionPart = QueryAutoCompactRunner.IsAutoCompactEnabled()
            ? "auto-compact on"
            : "manual compact";

        return $"context ~{usedPercent}% full · {FormatTokenCount(estimatedTokens)}/{FormatTokenCount(effectiveContextWindow)} tokens used · {compactionPart}";
    }

    private static string FormatTokenCount(int value)
    {
        if (value >= 1_000_000)
        {
            return $"{value / 1_000_000d:0.#}m";
        }

        if (value >= 1_000)
        {
            return $"{value / 1_000d:0.#}k";
        }

        return value.ToString();
    }
}
