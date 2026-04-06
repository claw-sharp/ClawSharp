namespace ClawSharp.Core;

public sealed record ApprovalRequest(
    string Id,
    string Action,
    ApprovalDecision Decision,
    DateTimeOffset CreatedAt);
