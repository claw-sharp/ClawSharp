namespace ClawSharp.AgentHost.Contracts;

public sealed record GetThreadResponse(
    ProjectSummaryDto Project,
    ThreadDetailDto Thread);
