namespace ClawSharp.AgentHost.Contracts;

public sealed class CancelRunRequest
{
    public string RunId { get; init; } = string.Empty;
}
