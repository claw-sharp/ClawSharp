namespace ClawSharp.Contracts.Runs;

public sealed record RetryRunRequest(
    string ThreadId,
    string? ProjectId = null,
    string? FromMessageId = null);
