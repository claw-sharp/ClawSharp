namespace ClawSharp.Core;

public sealed record SettingsLoadResult(
    ClawSharpSettings Settings,
    IReadOnlyList<SettingsLoadIssue> Issues,
    IReadOnlyDictionary<PermissionRuleSource, SettingsSourcePreferences> SourcePreferences);
