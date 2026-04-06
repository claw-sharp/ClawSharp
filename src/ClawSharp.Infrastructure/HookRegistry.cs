// TS origin: ./utils/hooks.ts, ./utils/hooks/hooksSettings.ts, ./utils/hooks/hooksConfigManager.ts
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class HookRegistry
{
    private static readonly IReadOnlyDictionary<string, int> SourcePriority = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["userSettings"] = 0,
        ["projectSettings"] = 1,
        ["localSettings"] = 2,
        ["policySettings"] = 3,
        ["settings"] = 4,
        ["plugin"] = 999
    };

    public IReadOnlyList<HookDefinition> GetHooksForEvent(
        IReadOnlyList<HookDefinition> hooks,
        HookEvent hookEvent,
        string? matcherValue = null)
    {
        return hooks
            .Where(hook => hook.Event == hookEvent)
            .Where(hook => Matches(hook.Matcher, matcherValue))
            .OrderBy(hook => GetSourcePriority(hook.Source))
            .ThenByDescending(hook => GetMatcherSpecificity(hook.Matcher))
            .ThenBy(hook => hook.Matcher ?? string.Empty, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool Matches(string? matcher, string? matcherValue)
    {
        if (string.IsNullOrWhiteSpace(matcher))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(matcherValue))
        {
            return false;
        }

        return string.Equals(matcher, matcherValue, StringComparison.Ordinal) ||
               string.Equals(matcher, "*", StringComparison.Ordinal);
    }

    private static int GetSourcePriority(string source)
    {
        return SourcePriority.TryGetValue(source, out var priority) ? priority : 500;
    }

    private static int GetMatcherSpecificity(string? matcher)
    {
        return string.IsNullOrWhiteSpace(matcher) || matcher == "*" ? 0 : 1;
    }
}
