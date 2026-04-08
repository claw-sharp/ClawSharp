namespace ClawSharp.AgentHost.Contracts;

public sealed class ListDiagnosticsRequest
{
    public string? ProjectId { get; init; }
    public string? ThreadId { get; init; }
}
