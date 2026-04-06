// TS origin: ./utils/permissions/permissionSetup.ts, ./services/analytics/growthbook.ts
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class LocalOnlyAutoModeGateProvider : IAutoModeGateProvider
{
    public bool IsBypassPermissionsModeDisabled()
    {
        return false;
    }

    public string? GetCachedAutoModeEnabledState()
    {
        return null;
    }

    public IReadOnlyList<string> GetAutoModeAllowModels()
    {
        return [];
    }
}
