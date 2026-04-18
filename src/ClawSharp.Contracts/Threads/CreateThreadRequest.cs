namespace ClawSharp.Contracts.Threads;

public sealed record CreateThreadRequest(
    string ProjectId,
    string? Title = null);
