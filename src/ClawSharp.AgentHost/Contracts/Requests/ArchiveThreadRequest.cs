namespace ClawSharp.AgentHost.Contracts;

public sealed class ArchiveThreadRequest
{
    public string ProjectId { get; init; } = string.Empty;
    public string ThreadId { get; init; } = string.Empty;
}
