namespace ClawSharp.AgentHost.Contracts;

public sealed record ProposeAgentResponse(
    string ProjectId,
    string WorkspaceRoot,
    AgentProposalDto Proposal);
