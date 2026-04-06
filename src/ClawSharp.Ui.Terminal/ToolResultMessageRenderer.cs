using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Tools;

namespace ClawSharp.Ui.Terminal;

public sealed class ToolResultMessageRenderer
{
    private readonly ToolRegistry _tools;

    public ToolResultMessageRenderer(ToolRegistry tools)
    {
        _tools = tools;
    }

    public RenderedToolResult? TryRender(
        ChatMessage message,
        IReadOnlyDictionary<string, string> toolNamesByToolUseId,
        IReadOnlyDictionary<string, List<ToolProgressUpdate>> progressMessagesByParentToolUseId)
    {
        if (message.Role != MessageRole.User || message.ContentBlocks.Count != 1)
        {
            return null;
        }

        var block = message.ContentBlocks[0];
        if (block.Kind != MessageContentKind.ToolResult ||
            block.Metadata is null ||
            !block.Metadata.TryGetValue("toolUseId", out var toolUseId) ||
            string.IsNullOrWhiteSpace(toolUseId))
        {
            return null;
        }

        var toolName = block.Name;
        if (string.IsNullOrWhiteSpace(toolName) && !toolNamesByToolUseId.TryGetValue(toolUseId, out toolName))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(toolName) ||
            !_tools.TryResolve(toolName, out var tool) ||
            tool is null)
        {
            return null;
        }

        JsonNode? structuredOutput = null;
        if (block.Metadata.TryGetValue("structuredOutput", out var structuredOutputJson) &&
            !string.IsNullOrWhiteSpace(structuredOutputJson))
        {
            structuredOutput = JsonNode.Parse(structuredOutputJson);
        }

        var progressMessages = progressMessagesByParentToolUseId.TryGetValue(toolUseId, out var recordedProgressMessages)
            ? recordedProgressMessages
            : [];
        var rendered = tool.RenderToolResultMessage(block.Value, structuredOutput, progressMessages);
        return string.IsNullOrWhiteSpace(rendered)
            ? null
            : new RenderedToolResult(toolUseId, rendered);
    }
}

public sealed record RenderedToolResult(string ToolUseId, string Content);
