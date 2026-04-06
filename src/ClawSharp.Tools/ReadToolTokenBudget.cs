namespace ClawSharp.Tools;

internal static class ReadToolTokenBudget
{
    public const int DefaultMaxOutputTokens = 25000;
    private const string MaxOutputTokensEnvironmentVariable = "CLAUDE_CODE_FILE_READ_MAX_OUTPUT_TOKENS";

    public static void Validate(string content, string fileExtension)
    {
        var effectiveMaxTokens = GetEffectiveMaxTokens();
        var tokenEstimate = RoughTokenCountEstimationForFileType(content, fileExtension);
        if (tokenEstimate <= effectiveMaxTokens / 4)
        {
            return;
        }

        // The current C# runtime does not yet have the TS model-backed token-count path.
        // Match the TS fallback path by enforcing the rough estimate when no exact count is available.
        if (tokenEstimate > effectiveMaxTokens)
        {
            throw new InvalidOperationException(
                $"File content ({tokenEstimate} tokens) exceeds maximum allowed tokens ({effectiveMaxTokens}). Use offset and limit parameters to read specific portions of the file, or search for specific content instead of reading the whole file.");
        }
    }

    private static int GetEffectiveMaxTokens()
    {
        var environmentOverride = Environment.GetEnvironmentVariable(MaxOutputTokensEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentOverride) &&
            int.TryParse(environmentOverride, out var parsedOverride) &&
            parsedOverride > 0)
        {
            return parsedOverride;
        }

        return DefaultMaxOutputTokens;
    }

    private static int RoughTokenCountEstimationForFileType(string content, string fileExtension)
    {
        return RoughTokenCountEstimation(content, BytesPerTokenForFileType(fileExtension));
    }

    private static int RoughTokenCountEstimation(string content, int bytesPerToken)
    {
        return (int)Math.Round((double)content.Length / bytesPerToken, MidpointRounding.AwayFromZero);
    }

    private static int BytesPerTokenForFileType(string fileExtension)
    {
        return NormalizeExtension(fileExtension) switch
        {
            "json" or "jsonl" or "jsonc" => 2,
            _ => 4
        };
    }

    private static string NormalizeExtension(string fileExtension)
    {
        return fileExtension.StartsWith(".", StringComparison.Ordinal)
            ? fileExtension[1..]
            : fileExtension;
    }
}
