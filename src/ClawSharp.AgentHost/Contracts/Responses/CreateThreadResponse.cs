namespace ClawSharp.AgentHost.Contracts;

public sealed record CreateThreadResponse(
    ProjectSummaryDto Project,
    ThreadDetailDto Thread);
