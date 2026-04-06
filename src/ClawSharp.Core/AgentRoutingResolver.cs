namespace ClawSharp.Core;

public static class AgentRoutingResolver
{
    public static string? ResolveRoutedModel(
        ClawSharpSettings settings,
        string? agentName,
        string? subagentType)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.AgentRouting.Count == 0 || settings.AgentModels.Count == 0)
        {
            return null;
        }

        var normalizedRouting = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in settings.AgentRouting)
        {
            var normalizedKey = Normalize(pair.Key);
            if (!normalizedRouting.ContainsKey(normalizedKey))
            {
                normalizedRouting[normalizedKey] = pair.Value;
            }
        }

        foreach (var candidate in EnumerateCandidates(agentName, subagentType))
        {
            if (!normalizedRouting.TryGetValue(Normalize(candidate), out var modelName) ||
                string.IsNullOrWhiteSpace(modelName))
            {
                continue;
            }

            var trimmedModelName = modelName.Trim();
            if (settings.AgentModels.ContainsKey(trimmedModelName))
            {
                return trimmedModelName;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidates(string? agentName, string? subagentType)
    {
        if (!string.IsNullOrWhiteSpace(agentName))
        {
            yield return agentName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(subagentType))
        {
            yield return subagentType.Trim();
        }

        yield return "default";
    }

    private static string Normalize(string value)
    {
        return string.Concat(
            value
                .Trim()
                .ToLowerInvariant()
                .Where(character => character is not '-' and not '_'));
    }
}
