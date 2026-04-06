// TS origin: ./tasks/pillLabel.ts, ./components/tasks/BackgroundTaskStatus.tsx
using ClawSharp.Tasks;

namespace ClawSharp.Ui.Terminal;

public sealed class BackgroundTaskSummaryRenderer
{
    private const string DiamondOpen = "\u25C7";

    public string? Render(IReadOnlyList<ClawSharpTask> tasks)
    {
        if (tasks.Count == 0)
        {
            return null;
        }

        var count = tasks.Count;
        var firstType = tasks[0].Type;
        var allSameType = tasks.All(task => task.Type == firstType);
        if (!allSameType)
        {
            return FormatGenericTaskCount(count);
        }

        return firstType switch
        {
            TaskType.LocalBash => RenderLocalBash(tasks),
            TaskType.LocalAgent => count == 1 ? "1 local agent" : $"{count} local agents",
            TaskType.RemoteAgent => count == 1
                ? $"{DiamondOpen} 1 cloud session"
                : $"{DiamondOpen} {count} cloud sessions",
            TaskType.LocalWorkflow => count == 1 ? "1 background workflow" : $"{count} background workflows",
            TaskType.MonitorMcp => count == 1 ? "1 monitor" : $"{count} monitors",
            TaskType.Dream => "dreaming",
            _ => FormatGenericTaskCount(count)
        };
    }

    private static string RenderLocalBash(IReadOnlyList<ClawSharpTask> tasks)
    {
        var monitors = tasks.Count(task => task is LocalBashTask { Kind: BashTaskKind.Monitor });
        var shells = tasks.Count - monitors;
        var parts = new List<string>();
        if (shells > 0)
        {
            parts.Add(shells == 1 ? "1 shell" : $"{shells} shells");
        }

        if (monitors > 0)
        {
            parts.Add(monitors == 1 ? "1 monitor" : $"{monitors} monitors");
        }

        return string.Join(", ", parts);
    }

    private static string FormatGenericTaskCount(int count)
    {
        return $"{count} background {(count == 1 ? "task" : "tasks")}";
    }
}
