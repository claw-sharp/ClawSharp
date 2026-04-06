// TS origin: ./types/permissions.ts
namespace ClawSharp.Core;

public enum PermissionRuleSource
{
    UserSettings,
    ProjectSettings,
    LocalSettings,
    FlagSettings,
    PolicySettings,
    CliArg,
    Command,
    Session
}
