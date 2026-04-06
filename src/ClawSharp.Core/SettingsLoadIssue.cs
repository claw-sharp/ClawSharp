// TS origin: ./utils/settings/validation.ts, ./utils/settings/settings.ts
namespace ClawSharp.Core;

public sealed record SettingsLoadIssue(
    string File,
    string Path,
    string Message);
