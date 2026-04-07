namespace ClawSharp.Core;

public sealed class PermissionSettings
{
    public IReadOnlyList<string> Allow { get; init; } = [];
    public IReadOnlyList<string> Deny { get; init; } = [];
    public IReadOnlyList<string> Ask { get; init; } = [];
    public PermissionMode? DefaultMode { get; init; }
    public string? DisableBypassPermissionsMode { get; init; }
    public string? DisableAutoMode { get; init; }
    public IReadOnlyList<string> AdditionalDirectories { get; init; } = [];
}
