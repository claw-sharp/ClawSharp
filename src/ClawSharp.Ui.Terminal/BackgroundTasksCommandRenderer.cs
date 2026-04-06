// TS origin: ./commands/tasks/tasks.tsx, ./components/tasks/BackgroundTasksDialog.tsx, ./components/tasks/BackgroundTask.tsx, ./components/tasks/taskStatusUtils.tsx
using ClawSharp.Core;
using ClawSharp.Tasks;
using System.Globalization;
using TaskStatus = ClawSharp.Tasks.TaskStatus;

namespace ClawSharp.Ui.Terminal;

public sealed class BackgroundTasksCommandRenderer
{
    private const int PromptCharacterLimit = 300;

    public string RenderList(IReadOnlyDictionary<string, ClawSharpTask> tasks)
    {
        var backgroundTasks = GetSortedBackgroundTasks(tasks).ToArray();
        if (backgroundTasks.Length == 0)
        {
            return "No background tasks.";
        }

        var lines = new List<string>
        {
            "Background tasks",
            RenderSummaryLine(backgroundTasks)
        };

        AppendGroup(lines, "teammates", backgroundTasks.Where(task => task.Type == TaskType.InProcessTeammate));
        AppendGroup(lines, "shells", backgroundTasks.Where(task => task.Type == TaskType.LocalBash));
        AppendGroup(lines, "monitors", backgroundTasks.Where(task => task.Type == TaskType.MonitorMcp));
        AppendGroup(lines, "remote agents", backgroundTasks.Where(task => task.Type == TaskType.RemoteAgent));
        AppendGroup(lines, "agents", backgroundTasks.Where(task => task.Type == TaskType.LocalAgent));
        AppendGroup(lines, "workflows", backgroundTasks.Where(task => task.Type == TaskType.LocalWorkflow));
        AppendGroup(lines, "dreams", backgroundTasks.Where(task => task.Type == TaskType.Dream));

        lines.Add(string.Empty);
        lines.Add("Use /tasks <task-id> to inspect a task.");
        return string.Join(Environment.NewLine, lines);
    }

    public string RenderDetail(ClawSharpTask task, string? outputContent)
    {
        return task switch
        {
            LocalAgentTask agentTask => RenderLocalAgentDetail(agentTask, outputContent),
            InProcessTeammateTask teammateTask => RenderTeammateDetail(teammateTask, outputContent),
            _ => RenderGenericDetail(task, outputContent)
        };
    }

    private static string RenderGenericDetail(ClawSharpTask task, string? outputContent)
    {
        var lines = new List<string>
        {
            $"{task.Id} [{task.Status.ToSerializedName()}] {task.Type.ToSerializedName()}",
            $"Description: {task.Description}",
            $"Started: {task.StartTime:O}"
        };

        if (task.EndTime is not null)
        {
            lines.Add($"Ended: {task.EndTime:O}");
        }

        if (!string.IsNullOrWhiteSpace(task.Prompt))
        {
            lines.Add($"Prompt: {task.Prompt}");
        }

        if (task is LocalBashTask bashTask)
        {
            lines.Add($"Command: {bashTask.Command}");
            lines.Add($"Kind: {bashTask.Kind.ToString().ToLowerInvariant()}");
        }
        else if (task is RemoteAgentTask remoteAgentTask)
        {
            lines.Add($"Remote session: {remoteAgentTask.SessionId}");
            lines.Add($"Title: {remoteAgentTask.Title}");
        }

        if (task.ExitCode is not null)
        {
            lines.Add($"Exit code: {task.ExitCode}");
        }

        if (!string.IsNullOrWhiteSpace(task.Error))
        {
            lines.Add($"Error: {task.Error}");
        }

        if (!string.IsNullOrWhiteSpace(task.Result))
        {
            lines.Add($"Result: {task.Result}");
        }

        if (!string.IsNullOrWhiteSpace(task.OutputFile))
        {
            lines.Add($"Output file: {task.OutputFile}");
        }

        lines.Add(string.Empty);
        lines.Add("Output");
        lines.Add(string.IsNullOrWhiteSpace(outputContent) ? "(no output yet)" : outputContent.TrimEnd());
        return string.Join(Environment.NewLine, lines);
    }

    private static string RenderLocalAgentDetail(LocalAgentTask task, string? outputContent)
    {
        var lines = new List<string>
        {
            $"{(string.IsNullOrWhiteSpace(task.AgentType) ? "agent" : task.AgentType)} \u203a {(string.IsNullOrWhiteSpace(task.Description) ? "Async agent" : task.Description)}",
            BuildSubtitle(task.Status, task.StartTime, task.EndTime, task.Progress?.TokenCount, task.Progress?.ToolUseCount)
        };

        AppendRecentActivities(lines, task.Status, task.Progress);

        var planContent = ExtractTagValue(task.Prompt, "<plan>", "</plan>");
        if (!string.IsNullOrWhiteSpace(planContent))
        {
            lines.Add(string.Empty);
            lines.Add("Plan");
            lines.Add(planContent.Trim());
        }
        else
        {
            AppendPromptSection(lines, task.Prompt);
        }

        AppendErrorSection(lines, task.Status, task.Error);
        AppendOutputSection(lines, outputContent);
        return string.Join(Environment.NewLine, lines);
    }

    private static string RenderTeammateDetail(InProcessTeammateTask task, string? outputContent)
    {
        var lines = new List<string>
        {
            $"@{task.Identity.AgentName} ({DescribeTeammateActivity(task)})",
            BuildSubtitle(task.Status, task.StartTime, task.EndTime, task.Progress?.TokenCount, task.Progress?.ToolUseCount)
        };

        AppendRecentActivities(lines, task.Status, task.Progress);
        AppendPromptSection(lines, TruncatePrompt(task.Prompt ?? string.Empty));
        AppendErrorSection(lines, task.Status, task.Error);
        AppendOutputSection(lines, outputContent);
        return string.Join(Environment.NewLine, lines);
    }

    private static IReadOnlyList<ClawSharpTask> GetSortedBackgroundTasks(IReadOnlyDictionary<string, ClawSharpTask> tasks)
    {
        return tasks.Values
            .Where(IsBackgroundTask)
            .OrderBy(task => task.Status == TaskStatus.Running ? 0 : task.Status == TaskStatus.Pending ? 1 : 2)
            .ThenByDescending(task => task.StartTime)
            .ToArray();
    }

    private static bool IsBackgroundTask(ClawSharpTask task)
    {
        return task.Type is TaskType.LocalBash or
            TaskType.RemoteAgent or
            TaskType.InProcessTeammate or
            TaskType.LocalWorkflow or
            TaskType.MonitorMcp or
            TaskType.Dream ||
            task is LocalAgentTask { IsBackgrounded: true };
    }

    private static string RenderSummaryLine(IReadOnlyList<ClawSharpTask> tasks)
    {
        var summaryParts = new List<string>();
        AppendSummary(summaryParts, tasks, TaskType.InProcessTeammate, "agent");
        AppendSummary(summaryParts, tasks, TaskType.LocalBash, "shell");
        AppendSummary(summaryParts, tasks, TaskType.MonitorMcp, "monitor");

        var remoteAgentCount = tasks.Count(task => task.Type == TaskType.RemoteAgent && task.Status is TaskStatus.Running or TaskStatus.Pending);
        if (remoteAgentCount > 0)
        {
            summaryParts.Add(remoteAgentCount == 1 ? "1 remote agent" : $"{remoteAgentCount} remote agents");
        }

        var agentCount = tasks.Count(task => task is LocalAgentTask { IsBackgrounded: true, Status: TaskStatus.Running });
        if (agentCount > 0)
        {
            summaryParts.Add(agentCount == 1 ? "1 agent" : $"{agentCount} agents");
        }

        AppendSummary(summaryParts, tasks, TaskType.LocalWorkflow, "workflow");
        AppendSummary(summaryParts, tasks, TaskType.Dream, "dream");
        return summaryParts.Count == 0 ? "No active background tasks." : string.Join(" · ", summaryParts);
    }

    private static void AppendSummary(
        ICollection<string> summaryParts,
        IReadOnlyList<ClawSharpTask> tasks,
        TaskType taskType,
        string label)
    {
        var count = tasks.Count(task => task.Type == taskType && task.Status == TaskStatus.Running);
        if (count == 0)
        {
            return;
        }

        summaryParts.Add(count == 1 ? $"1 {label}" : $"{count} {label}s");
    }

    private static void AppendGroup(List<string> lines, string title, IEnumerable<ClawSharpTask> tasks)
    {
        var groupedTasks = tasks.ToArray();
        if (groupedTasks.Length == 0)
        {
            return;
        }

        lines.Add(string.Empty);
        lines.Add(title);
        foreach (var task in groupedTasks)
        {
            lines.Add($"- {task.Id} [{task.Status.ToSerializedName()}] {BuildTaskLabel(task)}");
        }
    }

    private static string BuildTaskLabel(ClawSharpTask task)
    {
        return task switch
        {
            LocalBashTask bashTask => bashTask.Command,
            LocalAgentTask agentTask => agentTask.Description,
            InProcessTeammateTask teammateTask => $"@{teammateTask.Identity.AgentName}: {DescribeTeammateActivity(teammateTask)}",
            RemoteAgentTask remoteAgentTask => remoteAgentTask.Title,
            _ => task.Description
        };
    }

    private static string BuildSubtitle(
        TaskStatus status,
        DateTimeOffset startTime,
        DateTimeOffset? endTime,
        int? tokenCount,
        int? toolUseCount)
    {
        var parts = new List<string>();
        if (status != TaskStatus.Running)
        {
            parts.Add(status switch
            {
                TaskStatus.Completed => "Completed",
                TaskStatus.Failed => "Failed",
                _ => "Stopped"
            });
        }

        parts.Add(FormatDuration((endTime ?? DateTimeOffset.UtcNow) - startTime));

        if (tokenCount is > 0)
        {
            parts.Add($"{FormatCompactNumber(tokenCount.Value)} tokens");
        }

        if (toolUseCount is > 0)
        {
            parts.Add($"{toolUseCount.Value} {(toolUseCount.Value == 1 ? "tool" : "tools")}");
        }

        return string.Join(" \u00b7 ", parts);
    }

    private static void AppendRecentActivities(List<string> lines, TaskStatus status, AgentProgress? progress)
    {
        if (status != TaskStatus.Running || progress?.RecentActivities is null || progress.RecentActivities.Count == 0)
        {
            return;
        }

        lines.Add(string.Empty);
        lines.Add("Progress");

        for (var index = 0; index < progress.RecentActivities.Count; index++)
        {
            var prefix = index == progress.RecentActivities.Count - 1 ? "\u203a " : "  ";
            lines.Add($"{prefix}{RenderToolActivity(progress.RecentActivities[index])}");
        }
    }

    private static void AppendPromptSection(List<string> lines, string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return;
        }

        lines.Add(string.Empty);
        lines.Add("Prompt");
        lines.Add(TruncatePrompt(prompt));
    }

    private static void AppendErrorSection(List<string> lines, TaskStatus status, string? error)
    {
        if (status != TaskStatus.Failed || string.IsNullOrWhiteSpace(error))
        {
            return;
        }

        lines.Add(string.Empty);
        lines.Add("Error");
        lines.Add(error.Trim());
    }

    private static void AppendOutputSection(List<string> lines, string? outputContent)
    {
        lines.Add(string.Empty);
        lines.Add("Output");
        lines.Add(string.IsNullOrWhiteSpace(outputContent) ? "(no output yet)" : outputContent.TrimEnd());
    }

    private static string DescribeTeammateActivity(InProcessTeammateTask task)
    {
        if (task.ShutdownRequested)
        {
            return "stopping";
        }

        if (task.AwaitingPlanApproval)
        {
            return "awaiting approval";
        }

        if (task.IsIdle)
        {
            return "idle";
        }

        return SummarizeRecentActivities(task.Progress?.RecentActivities) ??
            task.Progress?.LastActivity?.ActivityDescription ??
            "working";
    }

    private static string? SummarizeRecentActivities(IReadOnlyList<ToolActivity>? activities)
    {
        if (activities is null || activities.Count == 0)
        {
            return null;
        }

        var searchCount = 0;
        var readCount = 0;
        for (var index = activities.Count - 1; index >= 0; index--)
        {
            var activity = activities[index];
            if (activity.IsSearch == true)
            {
                searchCount++;
            }
            else if (activity.IsRead == true)
            {
                readCount++;
            }
            else
            {
                break;
            }
        }

        if (searchCount + readCount >= 2)
        {
            return GetSearchReadSummaryText(searchCount, readCount);
        }

        for (var index = activities.Count - 1; index >= 0; index--)
        {
            if (!string.IsNullOrWhiteSpace(activities[index].ActivityDescription))
            {
                return activities[index].ActivityDescription;
            }
        }

        return null;
    }

    private static string GetSearchReadSummaryText(int searchCount, int readCount)
    {
        var parts = new List<string>();
        if (searchCount > 0)
        {
            parts.Add($"Searching for {searchCount} {(searchCount == 1 ? "pattern" : "patterns")}");
        }

        if (readCount > 0)
        {
            parts.Add($"{(parts.Count == 0 ? "Reading" : "reading")} {readCount} {(readCount == 1 ? "file" : "files")}");
        }

        return string.Join(", ", parts) + "\u2026";
    }

    private static string RenderToolActivity(ToolActivity activity)
    {
        if (!string.IsNullOrWhiteSpace(activity.ActivityDescription))
        {
            return activity.ActivityDescription;
        }

        return activity.ToolName;
    }

    private static string TruncatePrompt(string prompt)
    {
        return prompt.Length > PromptCharacterLimit
            ? prompt[..(PromptCharacterLimit - 1)] + "\u2026"
            : prompt;
    }

    private static string? ExtractTagValue(string? content, string openTag, string closeTag)
    {
        if (string.IsNullOrEmpty(content))
        {
            return null;
        }

        var startIndex = content.IndexOf(openTag, StringComparison.Ordinal);
        if (startIndex < 0)
        {
            return null;
        }

        startIndex += openTag.Length;
        var endIndex = content.IndexOf(closeTag, startIndex, StringComparison.Ordinal);
        if (endIndex < 0 || endIndex <= startIndex)
        {
            return null;
        }

        return content[startIndex..endIndex];
    }

    private static string FormatDuration(TimeSpan elapsed)
    {
        var milliseconds = Math.Max(0, elapsed.TotalMilliseconds);
        if (milliseconds < 60_000)
        {
            if (milliseconds == 0)
            {
                return "0s";
            }

            if (milliseconds < 1)
            {
                return $"{milliseconds / 1000:0.0}s";
            }

            return $"{Math.Floor(milliseconds / 1000)}s";
        }

        var totalMilliseconds = (long)Math.Round(milliseconds);
        var days = totalMilliseconds / 86_400_000;
        var hours = (totalMilliseconds % 86_400_000) / 3_600_000;
        var minutes = (totalMilliseconds % 3_600_000) / 60_000;
        var seconds = (int)Math.Round((totalMilliseconds % 60_000) / 1000d);

        if (seconds == 60)
        {
            seconds = 0;
            minutes++;
        }

        if (minutes == 60)
        {
            minutes = 0;
            hours++;
        }

        if (hours == 24)
        {
            hours = 0;
            days++;
        }

        if (days > 0)
        {
            return $"{days}d {hours}h {minutes}m";
        }

        if (hours > 0)
        {
            return $"{hours}h {minutes}m {seconds}s";
        }

        return $"{minutes}m {seconds}s";
    }

    private static string FormatCompactNumber(int value)
    {
        var absoluteValue = Math.Abs((double)value);
        var sign = value < 0 ? "-" : string.Empty;
        return absoluteValue switch
        {
            >= 1_000_000_000_000d => $"{sign}{(absoluteValue / 1_000_000_000_000d).ToString("0.0", CultureInfo.InvariantCulture)}t",
            >= 1_000_000_000d => $"{sign}{(absoluteValue / 1_000_000_000d).ToString("0.0", CultureInfo.InvariantCulture)}b",
            >= 1_000_000d => $"{sign}{(absoluteValue / 1_000_000d).ToString("0.0", CultureInfo.InvariantCulture)}m",
            >= 1_000d => $"{sign}{(absoluteValue / 1_000d).ToString("0.0", CultureInfo.InvariantCulture)}k",
            _ => $"{value}"
        };
    }

    private static string SerializePermissionMode(PermissionMode mode)
    {
        return mode switch
        {
            PermissionMode.Default => "default",
            PermissionMode.AcceptEdits => "acceptEdits",
            PermissionMode.BypassPermissions => "bypassPermissions",
            PermissionMode.DontAsk => "dontAsk",
            PermissionMode.Plan => "plan",
            PermissionMode.Auto => "auto",
            PermissionMode.Bubble => "bubble",
            _ => mode.ToString()
        };
    }
}
