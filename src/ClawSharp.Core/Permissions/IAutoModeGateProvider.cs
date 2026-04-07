namespace ClawSharp.Core;

public interface IAutoModeGateProvider
{
    bool IsBypassPermissionsModeDisabled();

    string? GetCachedAutoModeEnabledState();

    IReadOnlyList<string> GetAutoModeAllowModels();
}
