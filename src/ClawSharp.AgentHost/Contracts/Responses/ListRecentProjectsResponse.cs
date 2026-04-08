namespace ClawSharp.AgentHost.Contracts;

public sealed record ListRecentProjectsResponse(
    IReadOnlyList<ProjectSummaryDto> Projects);
