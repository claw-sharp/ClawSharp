// TS origin: ./tasks/LocalShellTask/LocalShellTask.tsx, ./tasks/LocalAgentTask/LocalAgentTask.tsx, ./tasks/RemoteAgentTask/RemoteAgentTask.tsx, ./utils/task/framework.ts, ./constants/xml.ts
using System.Text;

namespace ClawSharp.Tasks;

internal static class TaskNotificationFormatter
{
    private const string TaskNotificationTag = "task-notification";
    private const string TaskIdTag = "task-id";
    private const string ToolUseIdTag = "tool-use-id";
    private const string TaskTypeTag = "task-type";
    private const string OutputFileTag = "output-file";
    private const string StatusTag = "status";
    private const string SummaryTag = "summary";
    private const string WorktreeTag = "worktree";
    private const string WorktreePathTag = "worktreePath";
    private const string WorktreeBranchTag = "worktreeBranch";
    private const string BackgroundBashSummaryPrefix = "Background command ";

    public static string FormatLocalBash(
        LocalBashTask task,
        TaskStatus status)
    {
        var summary = task.Kind == BashTaskKind.Monitor
            ? status switch
            {
                TaskStatus.Completed => $"Monitor \"{task.Description}\" stream ended",
                TaskStatus.Failed => $"Monitor \"{task.Description}\" script failed{FormatMonitorExitCode(task.ExitCode)}",
                TaskStatus.Killed => $"Monitor \"{task.Description}\" stopped",
                _ => $"Monitor \"{task.Description}\" {status.ToSerializedName()}"
            }
            : status switch
            {
                TaskStatus.Completed => $"{BackgroundBashSummaryPrefix}\"{task.Description}\" completed{FormatBashExitCode(task.ExitCode)}",
                TaskStatus.Failed => $"{BackgroundBashSummaryPrefix}\"{task.Description}\" failed{FormatBashFailedExitCode(task.ExitCode)}",
                TaskStatus.Killed => $"{BackgroundBashSummaryPrefix}\"{task.Description}\" was stopped",
                _ => $"{BackgroundBashSummaryPrefix}\"{task.Description}\" {status.ToSerializedName()}"
            };

        var builder = new StringBuilder();
        builder.Append('<').Append(TaskNotificationTag).Append('>').AppendLine();
        builder.Append('<').Append(TaskIdTag).Append('>').Append(task.Id).Append("</").Append(TaskIdTag).Append('>');
        AppendToolUseIdLine(builder, task.ToolUseId);
        builder.AppendLine();
        builder.Append('<').Append(OutputFileTag).Append('>').Append(task.OutputFile).Append("</").Append(OutputFileTag).Append('>').AppendLine();
        builder.Append('<').Append(StatusTag).Append('>').Append(status.ToSerializedName()).Append("</").Append(StatusTag).Append('>').AppendLine();
        builder.Append('<').Append(SummaryTag).Append('>').Append(EscapeXmlText(summary)).Append("</").Append(SummaryTag).Append('>').AppendLine();
        builder.Append("</").Append(TaskNotificationTag).Append('>');
        return builder.ToString();
    }

    public static string FormatLocalAgent(
        LocalAgentTask task,
        TaskStatus status,
        string? error = null,
        string? finalMessage = null,
        TaskNotificationUsage? usage = null,
        string? worktreePath = null,
        string? worktreeBranch = null)
    {
        var resolvedError = error ?? task.Error;
        var summary = status switch
        {
            TaskStatus.Completed => $"Agent \"{task.Description}\" completed",
            TaskStatus.Failed => $"Agent \"{task.Description}\" failed: {resolvedError ?? "Unknown error"}",
            TaskStatus.Killed => $"Agent \"{task.Description}\" was stopped",
            _ => $"Agent \"{task.Description}\" {status.ToSerializedName()}"
        };

        var builder = new StringBuilder();
        builder.Append('<').Append(TaskNotificationTag).Append('>').AppendLine();
        builder.Append('<').Append(TaskIdTag).Append('>').Append(task.Id).Append("</").Append(TaskIdTag).Append('>');
        AppendToolUseIdLine(builder, task.ToolUseId);
        builder.AppendLine();
        builder.Append('<').Append(OutputFileTag).Append('>').Append(task.OutputFile).Append("</").Append(OutputFileTag).Append('>').AppendLine();
        builder.Append('<').Append(StatusTag).Append('>').Append(status.ToSerializedName()).Append("</").Append(StatusTag).Append('>').AppendLine();
        builder.Append('<').Append(SummaryTag).Append('>').Append(EscapeXmlText(summary)).Append("</").Append(SummaryTag).Append('>');

        if (!string.IsNullOrWhiteSpace(finalMessage))
        {
            builder.AppendLine()
                .Append("<result>")
                .Append(EscapeXmlText(finalMessage))
                .Append("</result>");
        }

        if (usage is not null)
        {
            builder.AppendLine()
                .Append("<usage><total_tokens>")
                .Append(usage.TotalTokens)
                .Append("</total_tokens><tool_uses>")
                .Append(usage.ToolUses)
                .Append("</tool_uses><duration_ms>")
                .Append(usage.DurationMs)
                .Append("</duration_ms></usage>");
        }

        if (!string.IsNullOrWhiteSpace(worktreePath))
        {
            builder.AppendLine()
                .Append('<').Append(WorktreeTag).Append('>')
                .Append('<').Append(WorktreePathTag).Append('>').Append(EscapeXmlText(worktreePath)).Append("</").Append(WorktreePathTag).Append('>');

            if (!string.IsNullOrWhiteSpace(worktreeBranch))
            {
                builder.Append('<').Append(WorktreeBranchTag).Append('>').Append(EscapeXmlText(worktreeBranch)).Append("</").Append(WorktreeBranchTag).Append('>');
            }

            builder.Append("</").Append(WorktreeTag).Append('>');
        }

        builder.AppendLine().Append("</").Append(TaskNotificationTag).Append('>');
        return builder.ToString();
    }

    public static string FormatRemoteAgent(
        RemoteAgentTask task,
        TaskStatus status)
    {
        var statusText = status switch
        {
            TaskStatus.Completed => "completed successfully",
            TaskStatus.Failed => "failed",
            TaskStatus.Killed => "was stopped",
            _ => status.ToSerializedName()
        };

        var builder = new StringBuilder();
        builder.Append('<').Append(TaskNotificationTag).Append('>').AppendLine();
        builder.Append('<').Append(TaskIdTag).Append('>').Append(task.Id).Append("</").Append(TaskIdTag).Append('>');
        AppendToolUseIdLine(builder, task.ToolUseId);
        builder.AppendLine();
        builder.Append('<').Append(TaskTypeTag).Append(">remote_agent</").Append(TaskTypeTag).Append('>').AppendLine();
        builder.Append('<').Append(OutputFileTag).Append('>').Append(task.OutputFile).Append("</").Append(OutputFileTag).Append('>').AppendLine();
        builder.Append('<').Append(StatusTag).Append('>').Append(status.ToSerializedName()).Append("</").Append(StatusTag).Append('>').AppendLine();
        builder.Append('<').Append(SummaryTag).Append('>').Append($"Remote task \"{EscapeXmlText(task.Title)}\" {statusText}").Append("</").Append(SummaryTag).Append('>').AppendLine();
        builder.Append("</").Append(TaskNotificationTag).Append('>');
        return builder.ToString();
    }

    private static void AppendToolUseIdLine(StringBuilder builder, string? toolUseId)
    {
        if (!string.IsNullOrWhiteSpace(toolUseId))
        {
            builder.AppendLine()
                .Append('<').Append(ToolUseIdTag).Append('>').Append(toolUseId).Append("</").Append(ToolUseIdTag).Append('>');
        }
    }

    private static string FormatBashExitCode(int? exitCode)
    {
        return exitCode is null ? string.Empty : $" (exit code {exitCode.Value})";
    }

    private static string FormatBashFailedExitCode(int? exitCode)
    {
        return exitCode is null ? string.Empty : $" with exit code {exitCode.Value}";
    }

    private static string FormatMonitorExitCode(int? exitCode)
    {
        return exitCode is null ? string.Empty : $" (exit {exitCode.Value})";
    }

    private static string EscapeXmlText(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }
}
