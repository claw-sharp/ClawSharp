// TS origin: ./utils/permissions/permissionSetup.ts, ./utils/settings/settings.ts, ./utils/betas.ts
using ClawSharp.Core;
using System.Text.RegularExpressions;

namespace ClawSharp.Infrastructure;

public static class AutoModePolicyEvaluator
{
    private static readonly PermissionRuleSource[] TrustedAutoModeSources =
    [
        PermissionRuleSource.PolicySettings,
        PermissionRuleSource.UserSettings,
        PermissionRuleSource.LocalSettings
    ];

    public static AutoModeGateState Evaluate(
        ClawSharpSettings settings,
        IReadOnlyDictionary<PermissionRuleSource, SettingsSourcePreferences> sourcePreferences,
        IAutoModeGateProvider gateProvider)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sourcePreferences);
        ArgumentNullException.ThrowIfNull(gateProvider);

        var autoModeAvailable = IsAutoModeGateEnabled(settings, gateProvider);
        var hasOptIn = HasAutoModeOptInAnySource(sourcePreferences);
        var useAutoModeDuringPlan = hasOptIn &&
                                    autoModeAvailable &&
                                    GetUseAutoModeDuringPlan(sourcePreferences);

        return new AutoModeGateState(
            autoModeAvailable,
            useAutoModeDuringPlan);
    }

    public static bool IsAutoModeGateEnabled(
        ClawSharpSettings settings,
        IAutoModeGateProvider gateProvider)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(gateProvider);

        if (string.Equals(gateProvider.GetCachedAutoModeEnabledState(), "disabled", StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(settings.Permissions.DisableAutoMode, "disable", StringComparison.Ordinal))
        {
            return false;
        }

        return ModelSupportsAutoMode(MainLoopModelResolver.Resolve(settings.Runtime.Model), gateProvider);
    }

    public static bool ShouldDisableBypassPermissions(
        ClawSharpSettings settings,
        IAutoModeGateProvider gateProvider)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(gateProvider);

        return gateProvider.IsBypassPermissionsModeDisabled() ||
               string.Equals(settings.Permissions.DisableBypassPermissionsMode, "disable", StringComparison.Ordinal);
    }

    private static bool HasAutoModeOptInAnySource(
        IReadOnlyDictionary<PermissionRuleSource, SettingsSourcePreferences> sourcePreferences)
    {
        foreach (var source in TrustedAutoModeSources)
        {
            if (sourcePreferences.TryGetValue(source, out var preferences) &&
                preferences.SkipAutoPermissionPrompt == true)
            {
                return true;
            }
        }

        return false;
    }

    private static bool GetUseAutoModeDuringPlan(
        IReadOnlyDictionary<PermissionRuleSource, SettingsSourcePreferences> sourcePreferences)
    {
        foreach (var source in TrustedAutoModeSources)
        {
            if (sourcePreferences.TryGetValue(source, out var preferences) &&
                preferences.UseAutoModeDuringPlan == false)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ModelSupportsAutoMode(string model, IAutoModeGateProvider gateProvider)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        var rawLower = model.ToLowerInvariant();
        var canonical = rawLower;
        foreach (var allowedModel in gateProvider.GetAutoModeAllowModels())
        {
            var allowedLower = allowedModel.ToLowerInvariant();
            if (allowedLower == rawLower || allowedLower == canonical)
            {
                return true;
            }
        }

        var userType = Environment.GetEnvironmentVariable("USER_TYPE");
        if (string.Equals(userType, "ant", StringComparison.Ordinal))
        {
            if (canonical.Contains("claude-3-", StringComparison.Ordinal))
            {
                return false;
            }

            return !Regex.IsMatch(
                canonical,
                @"claude-(opus|sonnet|haiku)-4(?!-[6-9])",
                RegexOptions.CultureInvariant);
        }

        return Regex.IsMatch(
            canonical,
            @"^claude-(opus|sonnet)-4-6",
            RegexOptions.CultureInvariant);
    }
}
