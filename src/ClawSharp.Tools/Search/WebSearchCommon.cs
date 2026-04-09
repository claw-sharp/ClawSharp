using System.Text.Json.Serialization;
using System.Text;
using System.Text.Json;

namespace ClawSharp.Tools;

public sealed record WebSearchInput(string Query, List<string>? AllowedDomains, List<string>? BlockedDomains);

public sealed record WebSearchResult(
    [property: JsonPropertyName("title")] string Title, 
    [property: JsonPropertyName("url")] string Url);

public sealed record WebSearchToolResult(
    [property: JsonPropertyName("tool_use_id")] string ToolUseId, 
    [property: JsonPropertyName("content")] List<WebSearchResult> Content);

public static class WebSearchFormatting
{
    public static string FormatOutputString(string query, List<object> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Web search results for query: \"{query}\"");
        sb.AppendLine();

        foreach (var result in results)
        {
            if (result is string text)
            {
                sb.AppendLine(text);
                sb.AppendLine();
            }
            else if (result is WebSearchToolResult toolResult)
            {
                if (toolResult.Content.Count > 0)
                {
                    sb.AppendLine($"Links: {JsonSerializer.Serialize(toolResult.Content)}");
                    sb.AppendLine();
                }
                else
                {
                    sb.AppendLine("No links found.");
                    sb.AppendLine();
                }
            }
        }

        sb.Append("\nREMINDER: You MUST include the sources above in your response to the user using markdown hyperlinks.");
        return sb.ToString().Trim();
    }
}
