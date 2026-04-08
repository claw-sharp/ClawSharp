namespace ClawSharp.AgentHost.Contracts;

public sealed record ArchiveThreadResponse(
    string ProjectId,
    string ThreadId,
    bool Archived,
    DateTimeOffset Timestamp);
