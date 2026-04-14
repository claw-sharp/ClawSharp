using System.Text;
using System.Text.RegularExpressions;

namespace ClawSharp.Tools;

internal static partial class ReadToolPdfFallbackReader
{
    private const int MinPrintableRunLength = 4;
    private const int MaxHintLines = 80;
    private const int MaxContentCharacters = 4_000;

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiWhitespacePattern();

    public static string Read(string filePath, string? requestedPages)
    {
        var bytes = File.ReadAllBytes(filePath);
        if (bytes.Length < 5 ||
            bytes[0] != (byte)'%' ||
            bytes[1] != (byte)'P' ||
            bytes[2] != (byte)'D' ||
            bytes[3] != (byte)'F' ||
            bytes[4] != (byte)'-')
        {
            throw new InvalidOperationException($"File is not a valid PDF (missing %PDF- header): {filePath}");
        }

        var extractedLines = ExtractPrintableRuns(bytes)
            .Select(NormalizeHintLine)
            .Where(IsUsefulHintLine)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxHintLines)
            .ToArray();

        var builder = new StringBuilder();
        builder.AppendLine("PDF page rendering is unavailable in the current runtime, so this is a lightweight text fallback.");
        builder.Append("Absolute path: ");
        builder.AppendLine(filePath.Replace('\\', '/'));

        if (!string.IsNullOrWhiteSpace(requestedPages))
        {
            builder.Append("Requested pages: ");
            builder.AppendLine(requestedPages);
            builder.AppendLine("Note: page-specific extraction could not be completed, so the hints below come from the whole PDF.");
        }

        var fileNameHint = BuildFileNameHint(filePath);
        if (!string.IsNullOrWhiteSpace(fileNameHint))
        {
            builder.Append("File name hint: ");
            builder.AppendLine(fileNameHint);
        }

        if (TryDetectPageCount(extractedLines, out var pageCount))
        {
            builder.Append("Detected page count: ");
            builder.AppendLine(pageCount.ToString());
        }

        if (extractedLines.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Recovered text hints:");
            foreach (var line in extractedLines)
            {
                builder.Append("- ");
                builder.AppendLine(line);
            }
        }
        else
        {
            builder.AppendLine();
            builder.AppendLine("No printable text hints could be recovered from this PDF.");
        }

        var content = builder.ToString().TrimEnd();
        if (content.Length > MaxContentCharacters)
        {
            content = content[..MaxContentCharacters].TrimEnd() + Environment.NewLine + "[truncated]";
        }

        return content;
    }

    private static IEnumerable<string> ExtractPrintableRuns(byte[] bytes)
    {
        var current = new StringBuilder();
        foreach (var value in bytes)
        {
            if (value is >= 32 and <= 126)
            {
                current.Append((char)value);
                continue;
            }

            if (current.Length >= MinPrintableRunLength)
            {
                yield return current.ToString();
            }

            current.Clear();
        }

        if (current.Length >= MinPrintableRunLength)
        {
            yield return current.ToString();
        }
    }

    private static string NormalizeHintLine(string value)
    {
        var normalized = MultiWhitespacePattern().Replace(value.Trim(), " ");
        return normalized;
    }

    private static bool IsUsefulHintLine(string value)
    {
        if (value.Length < MinPrintableRunLength)
        {
            return false;
        }

        if (!value.Any(char.IsLetter))
        {
            return false;
        }

        if (value.StartsWith("%PDF-", StringComparison.Ordinal) ||
            value.StartsWith("endobj", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("stream", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("endstream", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("xref", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("trailer", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("startxref", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("obj", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("<</", StringComparison.Ordinal) ||
            value.StartsWith("<<", StringComparison.Ordinal) ||
            value.StartsWith("/Type/", StringComparison.Ordinal) ||
            value.StartsWith("/Filter/", StringComparison.Ordinal))
        {
            return false;
        }

        if (value.Count(static character => character is '<' or '>' or '/' or '[' or ']') > value.Length / 3)
        {
            return false;
        }

        return true;
    }

    private static string BuildFileNameHint(string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        return Regex.Replace(fileName, @"[-_\.]+", " ").Trim();
    }

    private static bool TryDetectPageCount(IReadOnlyList<string> hintLines, out int pageCount)
    {
        foreach (var line in hintLines)
        {
            var match = Regex.Match(line, @"\bCount\s+(?<count>\d+)\b", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups["count"].Value, out pageCount))
            {
                return true;
            }
        }

        pageCount = 0;
        return false;
    }
}
