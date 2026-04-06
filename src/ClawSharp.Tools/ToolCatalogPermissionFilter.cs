// TS parity status: ports the blanket tool deny-rule matcher used by filterToolsByDenyRules/getDenyRuleForTool for the current C# registry catalog surface; full 1:1 parity still depends on a separate MCP permission-check name when SDK no-prefix mode is active.
using ClawSharp.Core;

namespace ClawSharp.Tools;

internal static class ToolCatalogPermissionFilter
{
    private static readonly IReadOnlyDictionary<string, string> LegacyToolNameAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Task"] = "Agent",
            ["AgentOutputTool"] = "TaskOutput",
            ["BashOutputTool"] = "TaskOutput"
        };

    public static IReadOnlyList<IClawSharpTool> FilterDeniedTools(
        IEnumerable<IClawSharpTool> tools,
        ToolPermissionContext permissionContext)
    {
        var denyRules = GetDenyRules(permissionContext);
        if (denyRules.Count == 0)
        {
            return tools.ToArray();
        }

        return tools
            .Where(tool => !MatchesAnyDenyRule(tool.Descriptor.Name, denyRules))
            .ToArray();
    }

    private static IReadOnlyList<PermissionRuleValue> GetDenyRules(ToolPermissionContext permissionContext)
    {
        return permissionContext.AlwaysDenyRules
            .Values
            .SelectMany(static rules => rules)
            .Select(PermissionRuleValueFromString)
            .Where(static rule => string.IsNullOrEmpty(rule.RuleContent))
            .ToArray();
    }

    private static bool MatchesAnyDenyRule(string toolName, IReadOnlyList<PermissionRuleValue> denyRules)
    {
        foreach (var denyRule in denyRules)
        {
            if (ToolMatchesRule(toolName, denyRule))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ToolMatchesRule(string toolName, PermissionRuleValue rule)
    {
        if (!string.IsNullOrEmpty(rule.RuleContent))
        {
            return false;
        }

        var normalizedToolName = NormalizeLegacyToolName(toolName);
        if (string.Equals(rule.ToolName, normalizedToolName, StringComparison.Ordinal))
        {
            return true;
        }

        return TryParseMcpInfo(rule.ToolName, out var ruleInfo) &&
               TryParseMcpInfo(normalizedToolName, out var toolInfo) &&
               string.Equals(ruleInfo.ServerName, toolInfo.ServerName, StringComparison.Ordinal) &&
               (ruleInfo.ToolName is null || string.Equals(ruleInfo.ToolName, "*", StringComparison.Ordinal));
    }

    private static string NormalizeLegacyToolName(string name)
    {
        return LegacyToolNameAliases.TryGetValue(name, out var alias) ? alias : name;
    }

    private static PermissionRuleValue PermissionRuleValueFromString(string ruleString)
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
            rawContent
                .Replace(@"\(", "(", StringComparison.Ordinal)
                .Replace(@"\)", ")", StringComparison.Ordinal)
                .Replace(@"\\", @"\", StringComparison.Ordinal));
    }

    private static bool TryParseMcpInfo(string name, out McpToolNameInfo info)
    {
        info = default;
        if (!name.StartsWith("mcp__", StringComparison.Ordinal))
        {
            return false;
        }

        var remainder = name["mcp__".Length..];
        if (remainder.Length == 0)
        {
            return false;
        }

        var separatorIndex = remainder.IndexOf("__", StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            info = new McpToolNameInfo(remainder, null);
            return true;
        }

        var serverName = remainder[..separatorIndex];
        var toolName = remainder[(separatorIndex + "__".Length)..];
        if (serverName.Length == 0 || toolName.Length == 0)
        {
            return false;
        }

        info = new McpToolNameInfo(serverName, toolName);
        return true;
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

    private readonly record struct McpToolNameInfo(string ServerName, string? ToolName);
}
