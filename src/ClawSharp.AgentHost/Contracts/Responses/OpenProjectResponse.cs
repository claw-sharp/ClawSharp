namespace ClawSharp.AgentHost.Contracts;

public sealed record OpenProjectResponse(
    ProjectSummaryDto Project,
    IReadOnlyList<ThreadSummaryDto> Threads);
