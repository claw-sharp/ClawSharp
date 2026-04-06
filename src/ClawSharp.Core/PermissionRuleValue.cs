// TS origin: ./utils/permissions/PermissionRule.ts, ./utils/permissions/permissionRuleParser.ts
namespace ClawSharp.Core;

public sealed record PermissionRuleValue(
    string ToolName,
    string? RuleContent = null);
