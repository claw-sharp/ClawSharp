namespace ClawSharp.AgentHost.Contracts;

public sealed record CancelRunResponse(
    string RunId,
    bool Cancelled,
    DateTimeOffset Timestamp);
