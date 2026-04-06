namespace ClawSharp.Core;

public sealed record SettingsLoadIssue(
    string File,
    string Path,
    string Message);
