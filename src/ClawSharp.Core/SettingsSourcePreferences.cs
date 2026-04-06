// TS origin: ./utils/settings/settings.ts
namespace ClawSharp.Core;

public sealed record SettingsSourcePreferences(
    bool? SkipAutoPermissionPrompt = null,
    bool? UseAutoModeDuringPlan = null);
