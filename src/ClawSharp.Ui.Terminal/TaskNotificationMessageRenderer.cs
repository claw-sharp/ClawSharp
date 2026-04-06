// TS origin: ./components/messages/UserAgentNotificationMessage.tsx, ./components/messages/UserTextMessage.tsx
namespace ClawSharp.Ui.Terminal;

public sealed class TaskNotificationMessageRenderer
{
    private const string TaskNotificationOpenTag = "<task-notification>";
    private const string SummaryOpenTag = "<summary>";
    private const string SummaryCloseTag = "</summary>";
    private const string BlackCircle = "\u25CF";

    public string? TryRender(string content)
    {
        if (string.IsNullOrWhiteSpace(content) ||
            !content.Contains(TaskNotificationOpenTag, StringComparison.Ordinal))
        {
            return null;
        }

        var summary = ExtractTagValue(content, SummaryOpenTag, SummaryCloseTag);
        if (string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        return $"{BlackCircle} {DecodeXmlEntities(summary.Trim())}";
    }

    private static string? ExtractTagValue(string content, string openTag, string closeTag)
    {
        var startIndex = content.IndexOf(openTag, StringComparison.Ordinal);
        if (startIndex < 0)
        {
            return null;
        }

        startIndex += openTag.Length;
        var endIndex = content.IndexOf(closeTag, startIndex, StringComparison.Ordinal);
        if (endIndex < 0 || endIndex <= startIndex)
        {
            return null;
        }

        return content[startIndex..endIndex];
    }

    private static string DecodeXmlEntities(string value)
    {
        return value
            .Replace("&quot;", "\"", StringComparison.Ordinal)
            .Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal)
            .Replace("&amp;", "&", StringComparison.Ordinal);
    }
}
