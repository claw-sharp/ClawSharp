namespace ClawSharp.Tools;

internal static class ToolUseRenderFormatting
{
    public static string Quote(string value)
    {
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    public static string TruncateCommand(string command, int maxLines = 2, int maxChars = 160)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return string.Empty;
        }

        var lines = command.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var truncatedByLine = lines.Length > maxLines;
        var text = truncatedByLine
            ? string.Join('\n', lines.Take(maxLines))
            : command;

        var truncatedByChar = text.Length > maxChars;
        if (truncatedByChar)
        {
            text = text[..maxChars];
        }

        return (truncatedByLine || truncatedByChar)
            ? $"{text.TrimEnd()}..."
            : text;
    }
}
