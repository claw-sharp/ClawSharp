using System.Text.RegularExpressions;

namespace ClawSharp.Bridge;

public static partial class BridgeDisplayTagUtilities
{
    public static string StripDisplayTags(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var result = XmlTagBlockPattern().Replace(text, string.Empty).Trim();
        return string.IsNullOrEmpty(result) ? text : result;
    }

    public static string StripDisplayTagsAllowEmpty(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return XmlTagBlockPattern().Replace(text, string.Empty).Trim();
    }

    public static string StripIdeContextTags(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return IdeContextTagsPattern().Replace(text, string.Empty).Trim();
    }

    [GeneratedRegex(@"<([a-z][\w-]*)(?:\s[^>]*)?>[\s\S]*?<\/\1>\n?", RegexOptions.CultureInvariant)]
    private static partial Regex XmlTagBlockPattern();

    [GeneratedRegex(@"<(ide_opened_file|ide_selection)(?:\s[^>]*)?>[\s\S]*?<\/\1>\n?", RegexOptions.CultureInvariant)]
    private static partial Regex IdeContextTagsPattern();
}
