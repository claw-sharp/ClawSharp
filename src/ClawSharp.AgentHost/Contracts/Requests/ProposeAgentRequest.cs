namespace ClawSharp.AgentHost.Contracts;

public sealed class ProposeAgentRequest
{
    public string? ProjectId { get; init; }
    public string? Prompt { get; init; }
    public string? Model { get; init; }
}
