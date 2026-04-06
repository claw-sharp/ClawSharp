// TS origin: ./utils/settings/settings.ts, ./utils/settings/validation.ts
namespace ClawSharp.Core;

public sealed record SettingsLoadResult(
    ClawSharpSettings Settings,
    IReadOnlyList<SettingsLoadIssue> Issues,
    IReadOnlyDictionary<PermissionRuleSource, SettingsSourcePreferences> SourcePreferences);
