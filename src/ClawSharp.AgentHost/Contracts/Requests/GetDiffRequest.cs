namespace ClawSharp.AgentHost.Contracts;

public sealed class GetDiffRequest
{
    public string ProjectId { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string? ThreadId { get; init; }
}
