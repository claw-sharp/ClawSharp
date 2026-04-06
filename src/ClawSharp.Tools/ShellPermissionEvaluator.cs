// TS origin: ./utils/permissions/permissions.ts, ./utils/permissions/shellRuleMatching.ts, ./utils/permissions/dangerousPatterns.ts, ./tools/BashTool/bashPermissions.ts, ./tools/PowerShellTool/powershellPermissions.ts
using ClawSharp.Core;
using System.Text;
using System.Text.RegularExpressions;

namespace ClawSharp.Tools;

internal static class ShellPermissionEvaluator
{
    private static readonly string[] DangerousBashPatterns =
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
        "ssh",
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
        "powershell",
        "pwsh",
        "invoke-expression",
        "iex",
        "invoke-command",
        "start-process",
        "invoke-webrequest",
        "iwr",
        "curl",
        "wget",
        "python",
        "node",
        "npm run",
        "yarn run",
        "pnpm run",
        "ssh"
    ];

    private static readonly PermissionRuleSource[] RuleSourceOrder =
    [
        PermissionRuleSource.UserSettings,
        PermissionRuleSource.ProjectSettings,
        PermissionRuleSource.LocalSettings,
        PermissionRuleSource.FlagSettings,
        PermissionRuleSource.PolicySettings,
        PermissionRuleSource.CliArg,
        PermissionRuleSource.Command,
        PermissionRuleSource.Session
    ];

    public static ShellPermissionDecision Evaluate(
        string toolName,
        string command,
        ToolPermissionContext permissionContext,
        bool dangerouslyDisableSandbox,
        bool caseInsensitive)
    {
        if (TryMatchRule(permissionContext.AlwaysDenyRules, toolName, command, caseInsensitive, out _))
        {
            return ShellPermissionDecision.Deny($"Permission to use {toolName} has been denied.");
        }

        if (TryMatchRule(permissionContext.AlwaysAskRules, toolName, command, caseInsensitive, out _))
        {
            return ShellPermissionDecision.Ask(ApprovalPromptText.CreateToolPermissionRequestMessage(toolName));
        }

        if (dangerouslyDisableSandbox)
        {
            return ShellPermissionDecision.Ask("Run outside of the sandbox");
        }

        if (ShouldBypassPermissions(permissionContext))
        {
            return ShellPermissionDecision.Allow();
        }

        if (TryMatchRule(permissionContext.AlwaysAllowRules, toolName, command, caseInsensitive, out var allowRule))
        {
            if (permissionContext.Mode == PermissionMode.Auto &&
                allowRule?.RuleContent is not null &&
                IsDangerousAllowRule(toolName, allowRule.RuleContent, caseInsensitive))
            {
                return ShellPermissionDecision.Ask(
                    ApprovalPromptText.CreateToolPermissionRequestMessage(toolName));
            }

            return ShellPermissionDecision.Allow();
        }

        return ShellPermissionDecision.Ask(
            ApprovalPromptText.CreateToolPermissionRequestMessage(toolName));
    }

    private static bool ShouldBypassPermissions(ToolPermissionContext permissionContext)
    {
        return permissionContext.Mode == PermissionMode.BypassPermissions ||
               (permissionContext.Mode == PermissionMode.Plan && permissionContext.IsBypassPermissionsModeAvailable);
    }

    private static bool TryMatchRule(
        IReadOnlyDictionary<PermissionRuleSource, IReadOnlyList<string>> ruleSets,
        string toolName,
        string command,
        bool caseInsensitive,
        out PermissionRuleValue? matchedRule)
    {
        foreach (var source in RuleSourceOrder)
        {
            if (!ruleSets.TryGetValue(source, out var rules))
            {
                continue;
            }

            foreach (var ruleString in rules)
            {
                var rule = PermissionRuleValueFromString(ruleString);
                if (!string.Equals(rule.ToolName, toolName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(rule.RuleContent))
                {
                    matchedRule = rule;
                    return true;
                }

                if (CommandMatchesRule(rule.RuleContent, command, caseInsensitive))
                {
                    matchedRule = rule;
                    return true;
                }
            }
        }

        matchedRule = null;
        return false;
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

    private static bool IsDangerousAllowRule(string toolName, string ruleContent, bool caseInsensitive)
    {
        var dangerousPatterns = string.Equals(toolName, "PowerShell", StringComparison.Ordinal)
            ? DangerousPowerShellPatterns
            : DangerousBashPatterns;

        foreach (var pattern in dangerousPatterns)
        {
            if (CommandMatchesRule(ruleContent, pattern, caseInsensitive) ||
                CommandMatchesRule(pattern, ruleContent, caseInsensitive) ||
                HasDangerousPrefix(ruleContent, pattern, caseInsensitive))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasDangerousPrefix(string ruleContent, string prefix, bool caseInsensitive)
    {
        var comparison = caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var trimmed = ruleContent.Trim();
        return trimmed.Equals(prefix, comparison) ||
               trimmed.StartsWith(prefix + ":*", comparison) ||
               trimmed.StartsWith(prefix + " *", comparison) ||
               trimmed.StartsWith(prefix + " -*", comparison) ||
               trimmed.StartsWith(prefix + "*", comparison);
    }

    private static bool CommandMatchesRule(string ruleContent, string command, bool caseInsensitive)
    {
        var prefix = PermissionRuleExtractPrefix(ruleContent);
        if (prefix is not null)
        {
            return command.Equals(prefix, caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
                   command.StartsWith(prefix, caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        if (HasWildcards(ruleContent))
        {
            return MatchWildcardPattern(ruleContent, command, caseInsensitive);
        }

        return string.Equals(ruleContent, command, caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string? PermissionRuleExtractPrefix(string permissionRule)
    {
        var match = Regex.Match(permissionRule, "^(.+):\\*$", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : null;
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

    private static bool HasWildcards(string pattern)
    {
        if (pattern.EndsWith(":*", StringComparison.Ordinal))
        {
            return false;
        }

        for (var index = 0; index < pattern.Length; index++)
        {
            if (pattern[index] != '*')
            {
                continue;
            }

            var backslashCount = 0;
            for (var current = index - 1; current >= 0 && pattern[current] == '\\'; current--)
            {
                backslashCount++;
            }

            if (backslashCount % 2 == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchWildcardPattern(string pattern, string command, bool caseInsensitive)
    {
        const string escapedStarPlaceholder = "\0ESCAPED_STAR\0";
        const string escapedBackslashPlaceholder = "\0ESCAPED_BACKSLASH\0";

        var trimmedPattern = pattern.Trim();
        var processed = new StringBuilder();

        for (var index = 0; index < trimmedPattern.Length; index++)
        {
            var character = trimmedPattern[index];
            if (character == '\\' && index + 1 < trimmedPattern.Length)
            {
                var nextCharacter = trimmedPattern[index + 1];
                if (nextCharacter == '*')
                {
                    processed.Append(escapedStarPlaceholder);
                    index++;
                    continue;
                }

                if (nextCharacter == '\\')
                {
                    processed.Append(escapedBackslashPlaceholder);
                    index++;
                    continue;
                }
            }

            processed.Append(character);
        }

        var escaped = Regex.Escape(processed.ToString()).Replace("\\*", ".*", StringComparison.Ordinal);
        escaped = escaped
            .Replace(escapedStarPlaceholder, "\\*", StringComparison.Ordinal)
            .Replace(escapedBackslashPlaceholder, "\\\\", StringComparison.Ordinal);

        if (escaped.EndsWith(" .*", StringComparison.Ordinal) &&
            Regex.Matches(processed.ToString(), "\\*").Count == 1)
        {
            escaped = escaped[..^3] + "( .*)?";
        }

        var options = RegexOptions.Singleline | RegexOptions.CultureInvariant;
        if (caseInsensitive)
        {
            options |= RegexOptions.IgnoreCase;
        }

        return Regex.IsMatch(command, $"^{escaped}$", options);
    }
}

internal sealed record ShellPermissionDecision(
    FileToolPermissionBehavior Behavior,
    string? Message)
{
    public static ShellPermissionDecision Allow()
    {
        return new ShellPermissionDecision(FileToolPermissionBehavior.Allow, null);
    }

    public static ShellPermissionDecision Ask(string message)
    {
        return new ShellPermissionDecision(FileToolPermissionBehavior.Ask, message);
    }

    public static ShellPermissionDecision Deny(string message)
    {
        return new ShellPermissionDecision(FileToolPermissionBehavior.Deny, message);
    }
}
