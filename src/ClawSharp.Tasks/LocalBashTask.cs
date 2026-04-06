namespace ClawSharp.Tasks;

public sealed record LocalBashTask(
    string Id,
    string Description,
    TaskStatus Status,
    DateTimeOffset StartTime,
    string OutputFile,
    string Command,
    BashTaskKind Kind = BashTaskKind.Bash,
    bool CompletionStatusSentInAttachment = false,
    LocalShellCommand? ShellCommand = null,
    int LastReportedTotalLines = 0,
    bool IsBackgrounded = true,
    string? AgentId = null,
    long OutputOffset = 0,
    bool Notified = false,
    string? ToolUseId = null,
    DateTimeOffset? EndTime = null,
    int? ExitCode = null)
    : ClawSharpTask(
        Id,
        TaskType.LocalBash,
        Description,
        Status,
        StartTime,
        OutputFile,
        OutputOffset,
        Notified,
        ToolUseId,
        EndTime,
        ExitCode);
