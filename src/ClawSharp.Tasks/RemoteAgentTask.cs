namespace ClawSharp.Tasks;

public sealed record RemoteAgentTask(
    string Id,
    string Description,
    TaskStatus Status,
    DateTimeOffset StartTime,
    string OutputFile,
    string SessionId,
    string Command,
    string Title,
    bool IsLongRunning = false,
    long OutputOffset = 0,
    bool Notified = false,
    string? ToolUseId = null,
    DateTimeOffset? EndTime = null,
    string? Error = null)
    : ClawSharpTask(
        Id,
        TaskType.RemoteAgent,
        Description,
        Status,
        StartTime,
        OutputFile,
        OutputOffset,
        Notified,
        ToolUseId,
        EndTime,
        ExitCode: null,
        Prompt: Command,
        Result: null,
        Error);
