namespace ClawSharp.Core;

public sealed record PermissionRuleValue(
    string ToolName,
    string? RuleContent = null);
