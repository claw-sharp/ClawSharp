namespace ClawSharp.Tasks;

public record ClawSharpTask(
    string Id,
    TaskType Type,
    string Description,
    TaskStatus Status,
    DateTimeOffset StartTime,
    string OutputFile,
    long OutputOffset = 0,
    bool Notified = false,
    string? ToolUseId = null,
    DateTimeOffset? EndTime = null,
    int? ExitCode = null,
    string? Prompt = null,
    string? Result = null,
    string? Error = null);
