namespace ClawSharp.Contracts.Runs;

public sealed record RunEventEnvelope(
    string RunId,
    string ThreadId,
    RunEventKind Kind,
    DateTimeOffset Timestamp,
    string? Message = null,
    string? TextDelta = null,
    string? ToolName = null,
    string? Detail = null);
