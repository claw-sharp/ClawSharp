namespace ClawSharp.Contracts.Threads;

public sealed record ThreadMessage(
    string Id,
    string ThreadId,
    string Role,
    string Content,
    DateTimeOffset Timestamp);
