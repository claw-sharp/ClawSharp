namespace ClawSharp.Core;

public sealed record AdditionalWorkingDirectory(
    string Path,
    PermissionRuleSource Source);
