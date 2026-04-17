namespace ClawSharp.AgentHost.Contracts;

public sealed record CreateAgentResponse(
    string ProjectId,
    string WorkspaceRoot,
    AgentSummaryDto Agent,
    IReadOnlyList<AgentSummaryDto> Agents);
