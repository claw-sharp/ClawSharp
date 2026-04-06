// TS origin: ./utils/permissions/permissionSetup.ts, ./services/analytics/growthbook.ts, ./utils/betas.ts
namespace ClawSharp.Core;

public interface IAutoModeGateProvider
{
    bool IsBypassPermissionsModeDisabled();

    string? GetCachedAutoModeEnabledState();

    IReadOnlyList<string> GetAutoModeAllowModels();
}
