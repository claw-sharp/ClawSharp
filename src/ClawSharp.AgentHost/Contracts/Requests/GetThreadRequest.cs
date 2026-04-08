namespace ClawSharp.AgentHost.Contracts;

public sealed class GetThreadRequest
{
    public string ThreadId { get; init; } = string.Empty;
    public string? ProjectId { get; init; }
}
