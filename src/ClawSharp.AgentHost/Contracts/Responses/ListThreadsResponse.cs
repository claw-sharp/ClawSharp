namespace ClawSharp.AgentHost.Contracts;

public sealed record ListThreadsResponse(
    ProjectSummaryDto Project,
    IReadOnlyList<ThreadSummaryDto> Threads);
