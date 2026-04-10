namespace ClawSharp.AgentHost.Contracts;

public sealed class GetThreadRequest
{
    public string ThreadId { get; init; } = string.Empty;
    public string? ProjectId { get; init; }
    public string? BeforeMessageId { get; init; }
    public int? PageSize { get; init; }
}
