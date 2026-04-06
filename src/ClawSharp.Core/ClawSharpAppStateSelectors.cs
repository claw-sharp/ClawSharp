// TS origin: ./state/selectors.ts
using ClawSharp.Tasks;

namespace ClawSharp.Core;

public static class ClawSharpAppStateSelectors
{
    public static ClawSharpTask? GetTask(ClawSharpAppState appState, string? taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return null;
        }

        return appState.Tasks.TryGetValue(taskId, out var task) ? task : null;
    }

    public static IReadOnlyList<ClawSharpTask> GetRunningTasks(ClawSharpAppState appState)
    {
        return appState.Tasks.Values
            .Where(task => !string.Equals(task.Id, appState.ForegroundedTaskId, StringComparison.Ordinal))
            .Where(task => task.Status is not ClawSharp.Tasks.TaskStatus.Completed
                and not ClawSharp.Tasks.TaskStatus.Failed
                and not ClawSharp.Tasks.TaskStatus.Killed)
            .OrderBy(task => task.StartTime)
            .ToArray();
    }

    public static string? GetActiveSessionDisplayName(ClawSharpAppState appState)
    {
        return !string.IsNullOrWhiteSpace(appState.ActiveSessionTitle)
            ? appState.ActiveSessionTitle
            : appState.ActiveSessionId;
    }
}
