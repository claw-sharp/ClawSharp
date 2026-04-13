namespace ClawSharp.AgentHost.Contracts;

public sealed record RunStartedEvent(
    string RunId,
    string ThreadId,
    string ProjectId,
    string Prompt,
    DateTimeOffset Timestamp);

public sealed record RunTextDeltaEvent(
    string RunId,
    string ThreadId,
    string Delta,
    DateTimeOffset Timestamp);

public sealed record RunMessageCompletedEvent(
    string RunId,
    string ThreadId,
    ThreadMessageDto Message,
    DateTimeOffset Timestamp);

public sealed record RunToolProgressEvent(
    string RunId,
    string ThreadId,
    string ToolUseId,
    string? ParentToolUseId,
    string ToolName,
    string Label,
    string? Input,
    string? Detail,
    string Stage,
    DateTimeOffset Timestamp);

public sealed record RunToolResultEvent(
    string RunId,
    string ThreadId,
    string ToolUseId,
    string ToolName,
    bool Success,
    string Content,
    DateTimeOffset Timestamp);

public sealed record RunCompletedEvent(
    string RunId,
    string ThreadId,
    string Reason,
    string? ErrorMessage,
    DateTimeOffset Timestamp);

public sealed record RunFailedEvent(
    string RunId,
    string ThreadId,
    string ErrorMessage,
    DateTimeOffset Timestamp);
