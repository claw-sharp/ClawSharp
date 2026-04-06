// TS origin: ./utils/permissions/permissionSetup.ts, ./utils/settings/settings.ts, ./utils/betas.ts
namespace ClawSharp.Core;

public sealed record AutoModeGateState(
    bool IsAutoModeAvailable,
    bool UseAutoModeDuringPlan);
