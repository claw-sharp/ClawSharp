namespace ClawSharp.AgentHost.Contracts;

public sealed class RenameThreadRequest
{
    public string ProjectId { get; init; } = string.Empty;
    public string ThreadId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
}
