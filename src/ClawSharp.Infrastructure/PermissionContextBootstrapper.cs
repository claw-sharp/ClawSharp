// TS origin: ./utils/permissions/permissionsLoader.ts, ./utils/permissions/permissionSetup.ts
using System.Text.Json;
using System.Text.Json.Serialization;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class PermissionContextBootstrapper
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    private readonly IAutoModeGateProvider _autoModeGateProvider;

    public PermissionContextBootstrapper(IAutoModeGateProvider? autoModeGateProvider = null)
    {
        _autoModeGateProvider = autoModeGateProvider ?? new LocalOnlyAutoModeGateProvider();
    }

    public ToolPermissionContext Load(
        string workspaceRoot,
        ClawSharpSettings settings,
        IReadOnlyDictionary<PermissionRuleSource, SettingsSourcePreferences>? sourcePreferences = null)
    {
        var mode = settings.Permissions.DefaultMode ?? settings.Runtime.PermissionMode;
        var context = ToolPermissionContexts.CreateEmpty(mode) with
        {
            IsBypassPermissionsModeAvailable = mode == PermissionMode.BypassPermissions,
            IsSandboxEnabledInSettings = settings.Sandbox.Enabled,
            AreUnsandboxedCommandsAllowed = settings.Sandbox.AllowUnsandboxedCommands
        };

        var allowManagedOnly = settings.AllowManagedPermissionRulesOnly;
        foreach (var source in GetOrderedSources())
        {
            if (allowManagedOnly && source != PermissionRuleSource.PolicySettings)
            {
                continue;
            }

            var partial = TryLoadPermissionSettings(GetSettingsPath(workspaceRoot, source));
            if (partial?.Permissions is null)
            {
                continue;
            }

            context = MergeSource(context, partial.Permissions, source);
        }

        if (AutoModePolicyEvaluator.ShouldDisableBypassPermissions(settings, _autoModeGateProvider))
        {
            context = context with
            {
                IsBypassPermissionsModeAvailable = false,
                Mode = context.Mode == PermissionMode.BypassPermissions ? PermissionMode.Default : context.Mode
            };
        }

        var autoModeGateState = AutoModePolicyEvaluator.Evaluate(
            settings,
            sourcePreferences ?? new Dictionary<PermissionRuleSource, SettingsSourcePreferences>(),
            _autoModeGateProvider);

        context = context with
        {
            IsAutoModeAvailable = autoModeGateState.IsAutoModeAvailable,
            UseAutoModeDuringPlan = autoModeGateState.UseAutoModeDuringPlan
        };

        if (!autoModeGateState.IsAutoModeAvailable && context.Mode == PermissionMode.Auto)
        {
            context = context with
            {
                Mode = PermissionMode.Default
            };
        }

        return context;
    }

    private static ToolPermissionContext MergeSource(
        ToolPermissionContext context,
        PermissionSettings permissions,
        PermissionRuleSource source)
    {
        var additionalWorkingDirectories = new Dictionary<string, AdditionalWorkingDirectory>(context.AdditionalWorkingDirectories, StringComparer.Ordinal);
        foreach (var directory in permissions.AdditionalDirectories)
        {
            additionalWorkingDirectories[directory] = new AdditionalWorkingDirectory(directory, source);
        }

        return context with
        {
            AdditionalWorkingDirectories = additionalWorkingDirectories,
            AlwaysAllowRules = ReplaceRules(context.AlwaysAllowRules, source, NormalizeRules(permissions.Allow)),
            AlwaysDenyRules = ReplaceRules(context.AlwaysDenyRules, source, NormalizeRules(permissions.Deny)),
            AlwaysAskRules = ReplaceRules(context.AlwaysAskRules, source, NormalizeRules(permissions.Ask))
        };
    }

    private static IReadOnlyDictionary<PermissionRuleSource, IReadOnlyList<string>> ReplaceRules(
        IReadOnlyDictionary<PermissionRuleSource, IReadOnlyList<string>> current,
        PermissionRuleSource source,
        IReadOnlyList<string> replacement)
    {
        var updated = new Dictionary<PermissionRuleSource, IReadOnlyList<string>>(current);
        updated[source] = replacement;
        return updated;
    }

    private static IReadOnlyList<string> NormalizeRules(IReadOnlyList<string> rules)
    {
        return rules
            .Where(static rule => !string.IsNullOrWhiteSpace(rule))
            .Select(static rule => PermissionRuleParser.PermissionRuleValueToString(PermissionRuleParser.PermissionRuleValueFromString(rule)))
            .ToArray();
    }

    private static PermissionSettingsEnvelope? TryLoadPermissionSettings(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PermissionSettingsEnvelope>(File.ReadAllText(filePath), SerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<PermissionRuleSource> GetOrderedSources()
    {
        yield return PermissionRuleSource.UserSettings;
        yield return PermissionRuleSource.ProjectSettings;
        yield return PermissionRuleSource.LocalSettings;
        yield return PermissionRuleSource.PolicySettings;
    }

    private static string GetSettingsPath(string workspaceRoot, PermissionRuleSource source)
    {
        return source switch
        {
            PermissionRuleSource.UserSettings => ClaudeConfigPaths.GetUserSettingsFilePath(),
            PermissionRuleSource.ProjectSettings => ClaudeConfigPaths.GetProjectSettingsFilePath(workspaceRoot),
            PermissionRuleSource.LocalSettings => ClaudeConfigPaths.GetLocalSettingsFilePath(workspaceRoot),
            PermissionRuleSource.PolicySettings => ClaudeConfigPaths.GetManagedSettingsFilePath(),
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };
    }

    private sealed class PermissionSettingsEnvelope
    {
        public PermissionSettings? Permissions { get; init; }
    }
}
