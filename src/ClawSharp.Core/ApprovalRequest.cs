// TS origin: ./types/permissions.ts, ./utils/permissions/permissions.ts
namespace ClawSharp.Core;

public sealed record ApprovalRequest(
    string Id,
    string Action,
    ApprovalDecision Decision,
    DateTimeOffset CreatedAt);
