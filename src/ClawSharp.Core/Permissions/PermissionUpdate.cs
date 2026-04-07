namespace ClawSharp.Core;

public abstract record PermissionUpdate(
    PermissionUpdateDestination Destination);

public sealed record AddPermissionRulesUpdate(
    PermissionUpdateDestination Destination,
    IReadOnlyList<PermissionRuleValue> Rules,
    PermissionBehavior Behavior)
    : PermissionUpdate(Destination);

public sealed record ReplacePermissionRulesUpdate(
    PermissionUpdateDestination Destination,
    IReadOnlyList<PermissionRuleValue> Rules,
    PermissionBehavior Behavior)
    : PermissionUpdate(Destination);

public sealed record RemovePermissionRulesUpdate(
    PermissionUpdateDestination Destination,
    IReadOnlyList<PermissionRuleValue> Rules,
    PermissionBehavior Behavior)
    : PermissionUpdate(Destination);

public sealed record SetPermissionModeUpdate(
    PermissionUpdateDestination Destination,
    PermissionMode Mode)
    : PermissionUpdate(Destination);

public sealed record AddPermissionDirectoriesUpdate(
    PermissionUpdateDestination Destination,
    IReadOnlyList<string> Directories)
    : PermissionUpdate(Destination);

public sealed record RemovePermissionDirectoriesUpdate(
    PermissionUpdateDestination Destination,
    IReadOnlyList<string> Directories)
    : PermissionUpdate(Destination);
