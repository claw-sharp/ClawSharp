namespace ClawSharp.AgentHost.Contracts;

public sealed class ListChangedFilesRequest
{
    public string ProjectId { get; init; } = string.Empty;
    public string? ThreadId { get; init; }
}
