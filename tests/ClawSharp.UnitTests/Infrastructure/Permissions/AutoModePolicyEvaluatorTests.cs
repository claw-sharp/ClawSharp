using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class AutoModePolicyEvaluatorTests
{
    [Fact]
    public void Evaluate_EnablesAutoMode_ForSupportedModel_AndTrustedOptIn()
    {
        var settings = new ClawSharpSettings
        {
            Runtime = new RuntimeSettings
            {
                Model = "claude-sonnet-4-6"
            }
        };

        var state = AutoModePolicyEvaluator.Evaluate(
            settings,
            new Dictionary<PermissionRuleSource, SettingsSourcePreferences>
            {
                [PermissionRuleSource.UserSettings] = new(SkipAutoPermissionPrompt: true)
            },
            new FakeAutoModeGateProvider());

        Assert.True(state.IsAutoModeAvailable);
        Assert.True(state.UseAutoModeDuringPlan);
    }

    [Fact]
    public void Evaluate_ExcludesProjectSettings_FromTrustedOptIn()
    {
        var settings = new ClawSharpSettings
        {
            Runtime = new RuntimeSettings
            {
                Model = "claude-sonnet-4-6"
            }
        };

        var state = AutoModePolicyEvaluator.Evaluate(
            settings,
            new Dictionary<PermissionRuleSource, SettingsSourcePreferences>
            {
                [PermissionRuleSource.ProjectSettings] = new(SkipAutoPermissionPrompt: true)
            },
            new FakeAutoModeGateProvider());

        Assert.True(state.IsAutoModeAvailable);
        Assert.False(state.UseAutoModeDuringPlan);
    }

    [Fact]
    public void Evaluate_HonorsTrustedUseAutoModeDuringPlanOptOut()
    {
        var settings = new ClawSharpSettings
        {
            Runtime = new RuntimeSettings
            {
                Model = "claude-sonnet-4-6"
            }
        };

        var state = AutoModePolicyEvaluator.Evaluate(
            settings,
            new Dictionary<PermissionRuleSource, SettingsSourcePreferences>
            {
                [PermissionRuleSource.UserSettings] = new(SkipAutoPermissionPrompt: true),
                [PermissionRuleSource.LocalSettings] = new(UseAutoModeDuringPlan: false)
            },
            new FakeAutoModeGateProvider());

        Assert.True(state.IsAutoModeAvailable);
        Assert.False(state.UseAutoModeDuringPlan);
    }

    [Fact]
    public void Evaluate_DisablesAutoMode_ForUnsupportedExternalModel()
    {
        var settings = new ClawSharpSettings
        {
            Runtime = new RuntimeSettings
            {
                Model = "claude-sonnet-4-5"
            }
        };

        var state = AutoModePolicyEvaluator.Evaluate(
            settings,
            new Dictionary<PermissionRuleSource, SettingsSourcePreferences>
            {
                [PermissionRuleSource.UserSettings] = new(SkipAutoPermissionPrompt: true)
            },
            new FakeAutoModeGateProvider());

        Assert.False(state.IsAutoModeAvailable);
        Assert.False(state.UseAutoModeDuringPlan);
    }

    [Fact]
    public void Evaluate_UsesAllowModelsGateOverride()
    {
        var settings = new ClawSharpSettings
        {
            Runtime = new RuntimeSettings
            {
                Model = "claude-custom-preview"
            }
        };

        var state = AutoModePolicyEvaluator.Evaluate(
            settings,
            new Dictionary<PermissionRuleSource, SettingsSourcePreferences>
            {
                [PermissionRuleSource.UserSettings] = new(SkipAutoPermissionPrompt: true)
            },
            new FakeAutoModeGateProvider(allowModels: ["claude-custom-preview"]));

        Assert.True(state.IsAutoModeAvailable);
        Assert.True(state.UseAutoModeDuringPlan);
    }

    [Fact]
    public void ShouldDisableBypassPermissions_UsesGateProviderOrSettings()
    {
        Assert.True(
            AutoModePolicyEvaluator.ShouldDisableBypassPermissions(
                new ClawSharpSettings(),
                new FakeAutoModeGateProvider(disableBypassPermissionsMode: true)));

        Assert.True(
            AutoModePolicyEvaluator.ShouldDisableBypassPermissions(
                new ClawSharpSettings
                {
                    Permissions = new PermissionSettings
                    {
                        DisableBypassPermissionsMode = "disable"
                    }
                },
                new FakeAutoModeGateProvider()));
    }

    private sealed class FakeAutoModeGateProvider : IAutoModeGateProvider
    {
        private readonly bool _disableBypassPermissionsMode;
        private readonly string? _cachedAutoModeEnabledState;
        private readonly IReadOnlyList<string> _allowModels;

        public FakeAutoModeGateProvider(
            bool disableBypassPermissionsMode = false,
            string? cachedAutoModeEnabledState = null,
            IReadOnlyList<string>? allowModels = null)
        {
            _disableBypassPermissionsMode = disableBypassPermissionsMode;
            _cachedAutoModeEnabledState = cachedAutoModeEnabledState;
            _allowModels = allowModels ?? [];
        }

        public bool IsBypassPermissionsModeDisabled()
        {
            return _disableBypassPermissionsMode;
        }

        public string? GetCachedAutoModeEnabledState()
        {
            return _cachedAutoModeEnabledState;
        }

        public IReadOnlyList<string> GetAutoModeAllowModels()
        {
            return _allowModels;
        }
    }
}
