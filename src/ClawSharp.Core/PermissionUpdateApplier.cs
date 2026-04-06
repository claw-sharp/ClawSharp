// TS origin: ./utils/permissions/PermissionUpdate.ts, ./utils/permissions/permissionRuleParser.ts

namespace ClawSharp.Core;

public static class PermissionUpdateApplier
{
    public static IReadOnlyList<PermissionRuleValue> ExtractRules(
        IReadOnlyList<PermissionUpdate>? updates)
    {
        if (updates is null || updates.Count == 0)
        {
            return [];
        }

        return updates
            .OfType<AddPermissionRulesUpdate>()
            .SelectMany(static update => update.Rules)
            .ToArray();
    }

    public static bool HasRules(IReadOnlyList<PermissionUpdate>? updates)
    {
        return ExtractRules(updates).Count > 0;
    }

    public static ToolPermissionContext ApplyPermissionUpdate(
        ToolPermissionContext context,
        PermissionUpdate update)
    {
        return update switch
        {
            SetPermissionModeUpdate setMode => context with
            {
                Mode = setMode.Mode
            },
            AddPermissionRulesUpdate addRules => ApplyRulesUpdate(
                context,
                addRules.Behavior,
                addRules.Destination,
                addRules.Rules,
                replace: false,
                remove: false),
            ReplacePermissionRulesUpdate replaceRules => ApplyRulesUpdate(
                context,
                replaceRules.Behavior,
                replaceRules.Destination,
                replaceRules.Rules,
                replace: true,
                remove: false),
            RemovePermissionRulesUpdate removeRules => ApplyRulesUpdate(
                context,
                removeRules.Behavior,
                removeRules.Destination,
                removeRules.Rules,
                replace: false,
                remove: true),
            AddPermissionDirectoriesUpdate addDirectories => ApplyAddDirectories(context, addDirectories),
            RemovePermissionDirectoriesUpdate removeDirectories => ApplyRemoveDirectories(context, removeDirectories),
            _ => context
        };
    }

    public static ToolPermissionContext ApplyPermissionUpdates(
        ToolPermissionContext context,
        IReadOnlyList<PermissionUpdate> updates)
    {
        var updatedContext = context;
        foreach (var update in updates)
        {
            updatedContext = ApplyPermissionUpdate(updatedContext, update);
        }

        return updatedContext;
    }

    public static bool SupportsPersistence(PermissionUpdateDestination destination)
    {
        return destination is
            PermissionUpdateDestination.UserSettings or
            PermissionUpdateDestination.ProjectSettings or
            PermissionUpdateDestination.LocalSettings;
    }

    private static ToolPermissionContext ApplyRulesUpdate(
        ToolPermissionContext context,
        PermissionBehavior behavior,
        PermissionUpdateDestination destination,
        IReadOnlyList<PermissionRuleValue> rules,
        bool replace,
        bool remove)
    {
        var source = ToRuleSource(destination);
        var targetRules = behavior switch
        {
            PermissionBehavior.Allow => new Dictionary<PermissionRuleSource, IReadOnlyList<string>>(context.AlwaysAllowRules),
            PermissionBehavior.Deny => new Dictionary<PermissionRuleSource, IReadOnlyList<string>>(context.AlwaysDenyRules),
            PermissionBehavior.Ask => new Dictionary<PermissionRuleSource, IReadOnlyList<string>>(context.AlwaysAskRules),
            _ => throw new ArgumentOutOfRangeException(nameof(behavior), behavior, null)
        };

        var ruleStrings = rules.Select(PermissionRuleValueToString).ToArray();
        var existingRules = targetRules.TryGetValue(source, out var existing)
            ? existing.ToList()
            : [];

        IReadOnlyList<string> updatedRules;
        if (replace)
        {
            updatedRules = ruleStrings;
        }
        else if (remove)
        {
            var rulesToRemove = new HashSet<string>(ruleStrings, StringComparer.Ordinal);
            updatedRules = existingRules.Where(rule => !rulesToRemove.Contains(rule)).ToArray();
        }
        else
        {
            updatedRules = [.. existingRules, .. ruleStrings];
        }

        targetRules[source] = updatedRules;

        return behavior switch
        {
            PermissionBehavior.Allow => context with { AlwaysAllowRules = targetRules },
            PermissionBehavior.Deny => context with { AlwaysDenyRules = targetRules },
            PermissionBehavior.Ask => context with { AlwaysAskRules = targetRules },
            _ => throw new ArgumentOutOfRangeException(nameof(behavior), behavior, null)
        };
    }

    private static ToolPermissionContext ApplyAddDirectories(
        ToolPermissionContext context,
        AddPermissionDirectoriesUpdate update)
    {
        var additionalDirectories = new Dictionary<string, AdditionalWorkingDirectory>(
            context.AdditionalWorkingDirectories,
            StringComparer.Ordinal);

        var source = ToRuleSource(update.Destination);
        foreach (var directory in update.Directories)
        {
            additionalDirectories[directory] = new AdditionalWorkingDirectory(directory, source);
        }

        return context with
        {
            AdditionalWorkingDirectories = additionalDirectories
        };
    }

    private static ToolPermissionContext ApplyRemoveDirectories(
        ToolPermissionContext context,
        RemovePermissionDirectoriesUpdate update)
    {
        var additionalDirectories = new Dictionary<string, AdditionalWorkingDirectory>(
            context.AdditionalWorkingDirectories,
            StringComparer.Ordinal);

        foreach (var directory in update.Directories)
        {
            additionalDirectories.Remove(directory);
        }

        return context with
        {
            AdditionalWorkingDirectories = additionalDirectories
        };
    }

    public static PermissionRuleSource ToRuleSource(PermissionUpdateDestination destination)
    {
        return destination switch
        {
            PermissionUpdateDestination.UserSettings => PermissionRuleSource.UserSettings,
            PermissionUpdateDestination.ProjectSettings => PermissionRuleSource.ProjectSettings,
            PermissionUpdateDestination.LocalSettings => PermissionRuleSource.LocalSettings,
            PermissionUpdateDestination.Session => PermissionRuleSource.Session,
            PermissionUpdateDestination.CliArg => PermissionRuleSource.CliArg,
            _ => throw new ArgumentOutOfRangeException(nameof(destination), destination, null)
        };
    }

    private static string PermissionRuleValueToString(PermissionRuleValue ruleValue)
    {
        if (string.IsNullOrEmpty(ruleValue.RuleContent))
        {
            return ruleValue.ToolName;
        }

        return $"{ruleValue.ToolName}({EscapeRuleContent(ruleValue.RuleContent)})";
    }

    private static string EscapeRuleContent(string content)
    {
        return content
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("(", @"\(", StringComparison.Ordinal)
            .Replace(")", @"\)", StringComparison.Ordinal);
    }
}
