// TS origin: ./Tool.ts, ./types/permissions.ts
namespace ClawSharp.Core;

public sealed record ToolPermissionContext(
    PermissionMode Mode,
    IReadOnlyDictionary<string, AdditionalWorkingDirectory> AdditionalWorkingDirectories,
    IReadOnlyDictionary<PermissionRuleSource, IReadOnlyList<string>> AlwaysAllowRules,
    IReadOnlyDictionary<PermissionRuleSource, IReadOnlyList<string>> AlwaysDenyRules,
    IReadOnlyDictionary<PermissionRuleSource, IReadOnlyList<string>> AlwaysAskRules,
    bool IsBypassPermissionsModeAvailable,
    bool? IsAutoModeAvailable = null,
    bool UseAutoModeDuringPlan = false,
    IReadOnlyDictionary<PermissionRuleSource, IReadOnlyList<string>>? StrippedDangerousRules = null,
    bool? ShouldAvoidPermissionPrompts = null,
    bool? AwaitAutomatedChecksBeforeDialog = null,
    PermissionMode? PrePlanMode = null,
    bool IsSandboxEnabledInSettings = false,
    bool AreUnsandboxedCommandsAllowed = true);
