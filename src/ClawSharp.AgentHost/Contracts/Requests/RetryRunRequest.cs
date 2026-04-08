namespace ClawSharp.AgentHost.Contracts;

public sealed class RetryRunRequest
{
    public string ThreadId { get; init; } = string.Empty;
    public string? ProjectId { get; init; }
    public string? FromMessageId { get; init; }
}
