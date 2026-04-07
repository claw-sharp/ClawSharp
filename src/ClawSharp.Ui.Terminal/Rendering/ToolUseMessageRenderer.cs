using System.Text;
using System.Text.Json;
using ClawSharp.Core;
using ClawSharp.Tools;

namespace ClawSharp.Ui.Terminal;

public sealed class ToolUseMessageRenderer
{
    private readonly ToolRegistry _tools;

    public ToolUseMessageRenderer(ToolRegistry tools)
    {
        _tools = tools;
    }

    public IReadOnlyList<RenderedToolUse> TryRender(ChatMessage message)
    {
        if (message.Role != MessageRole.Assistant)
        {
            return [];
        }

        List<RenderedToolUse>? rendered = null;
        foreach (var block in message.ContentBlocks)
        {
            if (block.Kind != MessageContentKind.ToolUse ||
                string.IsNullOrWhiteSpace(block.Name) ||
                block.Metadata is null ||
                !block.Metadata.TryGetValue("toolUseId", out var toolUseId) ||
                string.IsNullOrWhiteSpace(toolUseId))
            {
                continue;
            }

            var summary = TryRenderSummary(block.Name, block.Value);
            rendered ??= [];
            rendered.Add(new RenderedToolUse(toolUseId, FormatAssistantToolUse(block.Name, summary)));
        }

        return rendered ?? [];
    }

    private string? TryRenderSummary(string toolName, string arguments)
    {
        if (_tools.TryResolve(toolName, out var tool) && tool is not null)
        {
            var rendered = tool.RenderToolUseMessage(arguments);
            if (rendered is not null)
            {
                return rendered;
            }
        }

        return TryRenderFallback(arguments);
    }

    private static string? TryRenderFallback(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(arguments);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return arguments.Trim();
            }

            var root = document.RootElement;
            var parts = new List<string>();
            AddQuotedIfPresent(root, "file_path", parts);
            AddQuotedIfPresent(root, "path", parts);
            AddQuotedIfPresent(root, "pattern", parts);
            AddQuotedIfPresent(root, "url", parts);
            AddQuotedIfPresent(root, "query", parts);
            AddQuotedIfPresent(root, "task_id", parts);
            AddQuotedIfPresent(root, "command", parts);

            return parts.Count == 0 ? null : string.Join(", ", parts.Take(3));
        }
        catch (JsonException)
        {
            return arguments.Trim();
        }
    }

    private static void AddQuotedIfPresent(JsonElement root, string propertyName, ICollection<string> parts)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var rendered = propertyName == "command"
            ? TruncateCommand(text)
            : text.Trim();
        parts.Add($"{propertyName}: {Quote(rendered)}");
    }

    private static string FormatAssistantToolUse(string toolName, string? summary)
    {
        var content = string.IsNullOrWhiteSpace(summary)
            ? toolName
            : $"{toolName} ({summary})";
        return PrefixAssistantLines(content);
    }

    private static string PrefixAssistantLines(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var builder = new StringBuilder();
        var wroteLine = false;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (wroteLine)
            {
                builder.AppendLine();
                builder.Append("  ");
            }
            else
            {
                builder.Append("* ");
                wroteLine = true;
            }

            builder.Append(line);
        }

        return wroteLine ? builder.ToString() : "* ";
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static string TruncateCommand(string command, int maxLines = 2, int maxChars = 160)
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

public sealed record RenderedToolUse(string ToolUseId, string Content);
