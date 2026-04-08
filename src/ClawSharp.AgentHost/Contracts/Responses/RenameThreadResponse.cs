namespace ClawSharp.AgentHost.Contracts;

public sealed record RenameThreadResponse(
    ProjectSummaryDto Project,
    ThreadDetailDto Thread);
