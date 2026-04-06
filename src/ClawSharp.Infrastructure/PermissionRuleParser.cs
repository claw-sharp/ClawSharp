using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public static class PermissionRuleParser
{
    private static readonly IReadOnlyDictionary<string, string> LegacyToolNameAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Task"] = "Agent",
            ["AgentOutputTool"] = "TaskOutput",
            ["BashOutputTool"] = "TaskOutput"
        };

    public static string NormalizeLegacyToolName(string name)
    {
        return LegacyToolNameAliases.TryGetValue(name, out var alias) ? alias : name;
    }

    public static PermissionRuleValue PermissionRuleValueFromString(string ruleString)
    {
        var openParenIndex = FindFirstUnescapedChar(ruleString, '(');
        if (openParenIndex == -1)
        {
            return new PermissionRuleValue(NormalizeLegacyToolName(ruleString));
        }

        var closeParenIndex = FindLastUnescapedChar(ruleString, ')');
        if (closeParenIndex == -1 || closeParenIndex <= openParenIndex || closeParenIndex != ruleString.Length - 1)
        {
            return new PermissionRuleValue(NormalizeLegacyToolName(ruleString));
        }

        var toolName = ruleString[..openParenIndex];
        if (string.IsNullOrEmpty(toolName))
        {
            return new PermissionRuleValue(NormalizeLegacyToolName(ruleString));
        }

        var rawContent = ruleString[(openParenIndex + 1)..closeParenIndex];
        if (rawContent is "" or "*")
        {
            return new PermissionRuleValue(NormalizeLegacyToolName(toolName));
        }

        return new PermissionRuleValue(
            NormalizeLegacyToolName(toolName),
            UnescapeRuleContent(rawContent));
    }

    public static string PermissionRuleValueToString(PermissionRuleValue ruleValue)
    {
        if (string.IsNullOrEmpty(ruleValue.RuleContent))
        {
            return ruleValue.ToolName;
        }

        return $"{ruleValue.ToolName}({EscapeRuleContent(ruleValue.RuleContent)})";
    }

    public static string EscapeRuleContent(string content)
    {
        return content
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("(", @"\(", StringComparison.Ordinal)
            .Replace(")", @"\)", StringComparison.Ordinal);
    }

    public static string UnescapeRuleContent(string content)
    {
        return content
            .Replace(@"\(", "(", StringComparison.Ordinal)
            .Replace(@"\)", ")", StringComparison.Ordinal)
            .Replace(@"\\", @"\", StringComparison.Ordinal);
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
