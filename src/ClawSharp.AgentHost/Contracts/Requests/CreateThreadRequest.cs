namespace ClawSharp.AgentHost.Contracts;

public sealed class CreateThreadRequest
{
    public string ProjectId { get; init; } = string.Empty;
    public string? Title { get; init; }
}
