// TS origin: ./utils/context.ts, ./services/api/claude.ts
// TS parity status: ports the main query max-output-token defaulting and
// environment override logic needed for live Anthropic requests; the
// GrowthBook-gated capped-default branch remains intentionally deferred until
// the C# runtime has an equivalent dynamic gate source.
using ClawSharp.Core;

namespace ClawSharp.Query;

public static class QueryMaxOutputTokensResolver
{
    public const int MaxOutputTokensDefault = 32_000;
    public const int MaxOutputTokensUpperLimit = 64_000;

    public static int GetMaxOutputTokensForModel(string model)
    {
        var resolvedModel = MainLoopModelResolver.Resolve(model);
        var (defaultTokens, upperLimit) = GetModelMaxOutputTokens(resolvedModel);

        return GetBoundedIntEnvVar(
            "CLAUDE_CODE_MAX_OUTPUT_TOKENS",
            defaultTokens,
            upperLimit);
    }

    public static (int DefaultTokens, int UpperLimit) GetModelMaxOutputTokens(string model)
    {
        var normalized = MainLoopModelResolver.Resolve(model).Trim().ToLowerInvariant();

        if (normalized.Contains("opus-4-6", StringComparison.Ordinal))
        {
            return (64_000, 128_000);
        }

        if (normalized.Contains("sonnet-4-6", StringComparison.Ordinal))
        {
            return (32_000, 128_000);
        }

        if (normalized.Contains("opus-4-5", StringComparison.Ordinal) ||
            normalized.Contains("sonnet-4", StringComparison.Ordinal) ||
            normalized.Contains("haiku-4", StringComparison.Ordinal))
        {
            return (32_000, 64_000);
        }

        if (normalized.Contains("opus-4-1", StringComparison.Ordinal) ||
            normalized.Contains("opus-4-", StringComparison.Ordinal))
        {
            return (32_000, 32_000);
        }

        if (normalized.Contains("claude-3-opus", StringComparison.Ordinal))
        {
            return (4_096, 4_096);
        }

        if (normalized.Contains("claude-3-sonnet", StringComparison.Ordinal))
        {
            return (8_192, 8_192);
        }

        if (normalized.Contains("claude-3-haiku", StringComparison.Ordinal))
        {
            return (4_096, 4_096);
        }

        if (normalized.Contains("3-5-sonnet", StringComparison.Ordinal) ||
            normalized.Contains("3-5-haiku", StringComparison.Ordinal))
        {
            return (8_192, 8_192);
        }

        if (normalized.Contains("3-7-sonnet", StringComparison.Ordinal))
        {
            return (32_000, 64_000);
        }

        if (normalized.Contains("gpt-5", StringComparison.Ordinal) ||
            normalized.Contains("codex", StringComparison.Ordinal))
        {
            return (32_000, 128_000);
        }

        if (normalized.Contains("gpt-4.1", StringComparison.Ordinal) ||
            normalized.Contains("gpt-4o", StringComparison.Ordinal))
        {
            return (16_384, 65_536);
        }

        if (normalized.Contains("gemini", StringComparison.Ordinal))
        {
            return (8_192, 65_536);
        }

        return (MaxOutputTokensDefault, MaxOutputTokensUpperLimit);
    }

    private static int GetBoundedIntEnvVar(string name, int defaultValue, int upperLimit)
    {
        var rawValue = Environment.GetEnvironmentVariable(name);
        if (!int.TryParse(rawValue, out var parsedValue))
        {
            return defaultValue;
        }

        if (parsedValue <= 0)
        {
            return defaultValue;
        }

        return Math.Min(parsedValue, upperLimit);
    }
}
