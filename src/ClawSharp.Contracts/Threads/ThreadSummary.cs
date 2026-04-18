namespace ClawSharp.Contracts.Threads;

public sealed record ThreadSummary(
    string Id,
    string ProjectId,
    string Title,
    string Summary,
    ThreadStatus Status,
    ThreadTarget Target,
    string Provider,
    string Model,
    int MessageCount,
    DateTimeOffset LastUpdatedAt);
