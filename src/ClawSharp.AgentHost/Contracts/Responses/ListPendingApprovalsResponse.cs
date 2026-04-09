namespace ClawSharp.AgentHost.Contracts;

public sealed record ListPendingApprovalsResponse(
    IReadOnlyList<ApprovalRequestDto> Approvals);
