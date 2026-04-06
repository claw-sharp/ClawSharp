// TS origin: ./services/compact/prompt.ts
// TS parity status: ports the current TypeScript compact-summary post-processing helper by stripping the drafting <analysis> block and rewriting the <summary> wrapper into a plain-text summary header for reinsertion into query context.
using System.Text.RegularExpressions;

namespace ClawSharp.Query;

public static partial class QueryCompactSummaryFormatter
{
    public static string Format(string summary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);

        var formattedSummary = AnalysisBlockRegex().Replace(summary, string.Empty, 1);
        var summaryMatch = SummaryBlockRegex().Match(formattedSummary);
        if (summaryMatch.Success)
        {
            var content = summaryMatch.Groups["content"].Value;
            formattedSummary = SummaryBlockRegex().Replace(
                formattedSummary,
                $"Summary:{Environment.NewLine}{content.Trim()}",
                1);
        }

        formattedSummary = ExcessBlankLinesRegex().Replace(formattedSummary, $"{Environment.NewLine}{Environment.NewLine}");
        return formattedSummary.Trim();
    }

    [GeneratedRegex(@"<analysis>[\s\S]*?</analysis>", RegexOptions.CultureInvariant)]
    private static partial Regex AnalysisBlockRegex();

    [GeneratedRegex(@"<summary>(?<content>[\s\S]*?)</summary>", RegexOptions.CultureInvariant)]
    private static partial Regex SummaryBlockRegex();

    [GeneratedRegex(@"\r?\n\r?\n+", RegexOptions.CultureInvariant)]
    private static partial Regex ExcessBlankLinesRegex();
}
