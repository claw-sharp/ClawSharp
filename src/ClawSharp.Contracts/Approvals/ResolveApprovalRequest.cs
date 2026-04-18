namespace ClawSharp.Contracts.Approvals;

public sealed record ResolveApprovalRequest(
    string ApprovalId,
    ApprovalDecision Decision);
