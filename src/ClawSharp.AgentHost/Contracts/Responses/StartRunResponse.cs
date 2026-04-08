namespace ClawSharp.AgentHost.Contracts;

public sealed record StartRunResponse(
    string RunId,
    string ThreadId,
    DateTimeOffset AcceptedAt);
