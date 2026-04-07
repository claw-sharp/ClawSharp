// TS parity status: ports the current TypeScript max-tokens context-overflow parser and next-attempt adjustment logic used by the streamed model retry path.
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ClawSharp.Query;

public static partial class QueryMaxTokensContextOverflowParser
{
    private const int FloorOutputTokens = 3000;
    private const int SafetyBuffer = 1000;

    public static QueryMaxTokensContextOverflowData? TryParse(QueryModelApiException exception)
    {
        if ((int)exception.StatusCode != 400)
        {
            return null;
        }

        var errorMessage = TryExtractErrorMessage(exception.ResponseBody);
        if (string.IsNullOrWhiteSpace(errorMessage) ||
            !errorMessage.Contains(
                "input length and `max_tokens` exceed context limit",
                StringComparison.Ordinal))
        {
            return null;
        }

        var match = ContextOverflowRegex().Match(errorMessage);
        if (!match.Success ||
            !int.TryParse(match.Groups["inputTokens"].Value, out var inputTokens) ||
            !int.TryParse(match.Groups["maxTokens"].Value, out var maxTokens) ||
            !int.TryParse(match.Groups["contextLimit"].Value, out var contextLimit))
        {
            return null;
        }

        return new QueryMaxTokensContextOverflowData(inputTokens, maxTokens, contextLimit);
    }

    public static int? TryCalculateAdjustedMaxTokens(
        QueryMaxTokensContextOverflowData overflow,
        QueryThinkingConfig? thinking)
    {
        var availableContext = Math.Max(0, overflow.ContextLimit - overflow.InputTokens - SafetyBuffer);
        if (availableContext < FloorOutputTokens)
        {
            return null;
        }

        var minRequired =
            string.Equals(thinking?.Type, "enabled", StringComparison.Ordinal)
                ? (thinking?.BudgetTokens ?? 0) + 1
                : 1;

        return Math.Max(FloorOutputTokens, Math.Max(availableContext, minRequired));
    }

    private static string? TryExtractErrorMessage(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            var json = JsonNode.Parse(responseBody);
            return json?["error"]?["message"]?.GetValue<string>() ??
                   json?["message"]?.GetValue<string>() ??
                   responseBody;
        }
        catch
        {
            return responseBody;
        }
    }

    [GeneratedRegex(
        @"input length and `max_tokens` exceed context limit: (?<inputTokens>\d+) \+ (?<maxTokens>\d+) > (?<contextLimit>\d+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex ContextOverflowRegex();
}

public sealed record QueryMaxTokensContextOverflowData(
    int InputTokens,
    int MaxTokens,
    int ContextLimit);
