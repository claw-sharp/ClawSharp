namespace ClawSharp.Contracts.Approvals;

public sealed record ApprovalSummary(
    string Id,
    string ThreadId,
    string Action,
    ApprovalDecision Decision,
    DateTimeOffset CreatedAt);
