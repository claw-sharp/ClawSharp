// TS origin: ./utils/permissions/permissionSetup.ts, ./utils/permissions/PermissionUpdate.ts, ./utils/permissions/dangerousPatterns.ts
using System.Text.RegularExpressions;

namespace ClawSharp.Core;

public static class PermissionModeTransition
{
    private static readonly string[] CrossPlatformCodeExec =
    [
        "python",
        "python3",
        "python2",
        "node",
        "deno",
        "tsx",
        "ruby",
        "perl",
        "php",
        "lua",
        "npx",
        "bunx",
        "npm run",
        "yarn run",
        "pnpm run",
        "bun run",
        "bash",
        "sh",
        "ssh"
    ];

    private static readonly string[] DangerousBashPatterns =
    [
        .. CrossPlatformCodeExec,
        "zsh",
        "fish",
        "eval",
        "exec",
        "env",
        "xargs",
        "sudo"
    ];

    private static readonly string[] DangerousPowerShellPatterns =
    [
        .. CrossPlatformCodeExec,
        "pwsh",
        "powershell",
        "cmd",
        "wsl",
        "iex",
        "invoke-expression",
        "icm",
        "invoke-command",
        "start-process",
        "saps",
        "start",
        "start-job",
        "sajb",
        "start-threadjob",
        "register-objectevent",
        "register-engineevent",
        "register-wmievent",
        "register-scheduledjob",
        "new-pssession",
        "nsn",
        "enter-pssession",
        "etsn",
        "add-type",
        "new-object"
    ];

    private static readonly PermissionRuleSource[] RestorableSources =
    [
        PermissionRuleSource.UserSettings,
        PermissionRuleSource.ProjectSettings,
        PermissionRuleSource.LocalSettings,
        PermissionRuleSource.Session,
        PermissionRuleSource.CliArg
    ];

    public static ToolPermissionContext Transition(
        ToolPermissionContext context,
        PermissionMode targetMode,
        bool useAutoModeDuringPlan = false)
    {
        if (context.Mode == PermissionMode.Plan && targetMode != PermissionMode.Plan)
        {
            return ExitPlanMode(context, targetMode);
        }

        if (context.Mode == targetMode)
        {
            return context;
        }

        if (targetMode == PermissionMode.Plan)
        {
            var planned = PrepareForPlanMode(context, useAutoModeDuringPlan);
            return planned with { Mode = PermissionMode.Plan };
        }

        if (targetMode == PermissionMode.Auto)
        {
            return StripDangerousPermissionsForAutoMode(context) with { Mode = PermissionMode.Auto };
        }

        if (context.Mode == PermissionMode.Auto)
        {
            return RestoreDangerousPermissions(context) with
            {
                Mode = targetMode,
                PrePlanMode = null
            };
        }

        return context with
        {
            Mode = targetMode,
            PrePlanMode = null
        };
    }

    public static ToolPermissionContext CreateDisabledBypassPermissionsContext(ToolPermissionContext context)
    {
        var mode = context.Mode == PermissionMode.BypassPermissions
            ? PermissionMode.Default
            : context.Mode;

        return context with
        {
            Mode = mode,
            IsBypassPermissionsModeAvailable = false
        };
    }

    public static ToolPermissionContext PrepareForPlanMode(
        ToolPermissionContext context,
        bool useAutoModeDuringPlan)
    {
        if (context.Mode == PermissionMode.Plan)
        {
            return context;
        }

        if (context.Mode == PermissionMode.Auto)
        {
            if (useAutoModeDuringPlan)
            {
                return context with { PrePlanMode = PermissionMode.Auto };
            }

            return RestoreDangerousPermissions(context) with { PrePlanMode = PermissionMode.Auto };
        }

        if (useAutoModeDuringPlan && context.Mode != PermissionMode.BypassPermissions)
        {
            return StripDangerousPermissionsForAutoMode(context) with { PrePlanMode = context.Mode };
        }

        return context with { PrePlanMode = context.Mode };
    }

    public static ToolPermissionContext StripDangerousPermissionsForAutoMode(ToolPermissionContext context)
    {
        var strippedRules = new Dictionary<PermissionRuleSource, IReadOnlyList<string>>();
        var allowRules = new Dictionary<PermissionRuleSource, IReadOnlyList<string>>(context.AlwaysAllowRules);

        foreach (var source in RestorableSources)
        {
            if (!allowRules.TryGetValue(source, out var rules) || rules.Count == 0)
            {
                continue;
            }

            var kept = new List<string>();
            var removed = new List<string>();
            foreach (var ruleString in rules)
            {
                var rule = PermissionRuleValueFromString(ruleString);
                if (IsDangerousClassifierPermission(rule.ToolName, rule.RuleContent))
                {
                    removed.Add(PermissionRuleValueToString(rule));
                    continue;
                }

                kept.Add(ruleString);
            }

            if (removed.Count > 0)
            {
                strippedRules[source] = removed.ToArray();
                allowRules[source] = kept.ToArray();
            }
        }

        return strippedRules.Count == 0
            ? context with
            {
                StrippedDangerousRules = context.StrippedDangerousRules ??
                                         new Dictionary<PermissionRuleSource, IReadOnlyList<string>>()
            }
            : context with
            {
                AlwaysAllowRules = allowRules,
                StrippedDangerousRules = strippedRules
            };
    }

    public static ToolPermissionContext RestoreDangerousPermissions(ToolPermissionContext context)
    {
        if (context.StrippedDangerousRules is null || context.StrippedDangerousRules.Count == 0)
        {
            return context;
        }

        var allowRules = new Dictionary<PermissionRuleSource, IReadOnlyList<string>>(context.AlwaysAllowRules);
        foreach (var (source, rules) in context.StrippedDangerousRules)
        {
            if (rules.Count == 0)
            {
                continue;
            }

            var combined = new List<string>();
            if (allowRules.TryGetValue(source, out var existing))
            {
                combined.AddRange(existing);
            }

            foreach (var rule in rules)
            {
                if (!combined.Contains(rule, StringComparer.Ordinal))
                {
                    combined.Add(rule);
                }
            }

            allowRules[source] = combined.ToArray();
        }

        return context with
        {
            AlwaysAllowRules = allowRules,
            StrippedDangerousRules = null
        };
    }

    private static ToolPermissionContext ExitPlanMode(ToolPermissionContext context, PermissionMode targetMode)
    {
        var resumedMode = targetMode == PermissionMode.Default && context.PrePlanMode.HasValue
            ? context.PrePlanMode.Value
            : targetMode;

        var baseContext = context with { PrePlanMode = null };
        if (resumedMode == PermissionMode.Auto)
        {
            return StripDangerousPermissionsForAutoMode(baseContext) with { Mode = PermissionMode.Auto };
        }

        return RestoreDangerousPermissions(baseContext) with { Mode = resumedMode };
    }

    private static bool IsDangerousClassifierPermission(string toolName, string? ruleContent)
    {
        return IsDangerousBashPermission(toolName, ruleContent) ||
               IsDangerousPowerShellPermission(toolName, ruleContent) ||
               string.Equals(toolName, "Agent", StringComparison.Ordinal);
    }

    private static bool IsDangerousBashPermission(string toolName, string? ruleContent)
    {
        if (!string.Equals(toolName, "Bash", StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(ruleContent))
        {
            return true;
        }

        var content = ruleContent.Trim().ToLowerInvariant();
        if (content == "*")
        {
            return true;
        }

        return DangerousBashPatterns.Any(pattern => MatchesDangerousPattern(content, pattern));
    }

    private static bool IsDangerousPowerShellPermission(string toolName, string? ruleContent)
    {
        if (!string.Equals(toolName, "PowerShell", StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(ruleContent))
        {
            return true;
        }

        var content = ruleContent.Trim().ToLowerInvariant();
        if (content == "*")
        {
            return true;
        }

        foreach (var pattern in DangerousPowerShellPatterns)
        {
            if (MatchesDangerousPattern(content, pattern))
            {
                return true;
            }

            var firstSpace = pattern.IndexOf(' ');
            var executablePattern = firstSpace == -1
                ? $"{pattern}.exe"
                : $"{pattern[..firstSpace]}.exe{pattern[firstSpace..]}";
            if (MatchesDangerousPattern(content, executablePattern))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesDangerousPattern(string content, string pattern)
    {
        return content == pattern ||
               content == $"{pattern}:*" ||
               content == $"{pattern}*" ||
               content == $"{pattern} *" ||
               (content.StartsWith($"{pattern} -", StringComparison.Ordinal) && content.EndsWith('*'));
    }

    private static PermissionRuleValue PermissionRuleValueFromString(string ruleString)
    {
        var openParenIndex = FindFirstUnescapedChar(ruleString, '(');
        if (openParenIndex == -1)
        {
            return new PermissionRuleValue(ruleString);
        }

        var closeParenIndex = FindLastUnescapedChar(ruleString, ')');
        if (closeParenIndex == -1 || closeParenIndex <= openParenIndex || closeParenIndex != ruleString.Length - 1)
        {
            return new PermissionRuleValue(ruleString);
        }

        var toolName = ruleString[..openParenIndex];
        if (string.IsNullOrEmpty(toolName))
        {
            return new PermissionRuleValue(ruleString);
        }

        var rawContent = ruleString[(openParenIndex + 1)..closeParenIndex];
        if (rawContent is "" or "*")
        {
            return new PermissionRuleValue(toolName);
        }

        return new PermissionRuleValue(
            toolName,
            rawContent
                .Replace(@"\(", "(", StringComparison.Ordinal)
                .Replace(@"\)", ")", StringComparison.Ordinal)
                .Replace(@"\\", @"\", StringComparison.Ordinal));
    }

    private static string PermissionRuleValueToString(PermissionRuleValue ruleValue)
    {
        if (string.IsNullOrEmpty(ruleValue.RuleContent))
        {
            return ruleValue.ToolName;
        }

        return $"{ruleValue.ToolName}({ruleValue.RuleContent.Replace(@"\", @"\\", StringComparison.Ordinal).Replace("(", @"\(", StringComparison.Ordinal).Replace(")", @"\)", StringComparison.Ordinal)})";
    }

    private static int FindFirstUnescapedChar(string value, char character)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != character)
            {
                continue;
            }

            var backslashCount = 0;
            for (var current = index - 1; current >= 0 && value[current] == '\\'; current--)
            {
                backslashCount++;
            }

            if (backslashCount % 2 == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindLastUnescapedChar(string value, char character)
    {
        for (var index = value.Length - 1; index >= 0; index--)
        {
            if (value[index] != character)
            {
                continue;
            }

            var backslashCount = 0;
            for (var current = index - 1; current >= 0 && value[current] == '\\'; current--)
            {
                backslashCount++;
            }

            if (backslashCount % 2 == 0)
            {
                return index;
            }
        }

        return -1;
    }
}
