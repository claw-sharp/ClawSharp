namespace ClawSharp.AgentHost.Contracts;

public sealed class StartRunRequest
{
    public string ThreadId { get; init; } = string.Empty;
    public string ProjectId { get; init; } = string.Empty;
    public string Prompt { get; init; } = string.Empty;
}
