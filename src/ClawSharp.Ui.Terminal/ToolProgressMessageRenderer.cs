// TS origin: ./components/messages/AssistantToolUseMessage.tsx, ./tools/TaskOutputTool/TaskOutputTool.tsx
using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Tools;

namespace ClawSharp.Ui.Terminal;

public sealed class ToolProgressMessageRenderer
{
    private readonly ToolRegistry _tools;
    private readonly TerminalProgressIndicatorRenderer _progressIndicatorRenderer;

    public ToolProgressMessageRenderer(ToolRegistry tools, TerminalProgressIndicatorRenderer? progressIndicatorRenderer = null)
    {
        _tools = tools;
        _progressIndicatorRenderer = progressIndicatorRenderer ?? new TerminalProgressIndicatorRenderer();
    }

    public void TrackToolUses(ChatMessage message, IDictionary<string, string> toolNamesByToolUseId)
    {
        if (message.Role != MessageRole.Assistant)
        {
            return;
        }

        foreach (var block in message.ContentBlocks)
        {
            if (block.Kind != MessageContentKind.ToolUse || string.IsNullOrWhiteSpace(block.Name))
            {
                continue;
            }

            if (block.Metadata is null || !block.Metadata.TryGetValue("toolUseId", out var toolUseId) || string.IsNullOrWhiteSpace(toolUseId))
            {
                continue;
            }

            toolNamesByToolUseId[toolUseId] = block.Name;
        }
    }

    public RenderedToolProgress? TryRender(
        ChatMessage message,
        IReadOnlyDictionary<string, string> toolNamesByToolUseId,
        IDictionary<string, List<ToolProgressUpdate>> progressMessagesByParentToolUseId)
    {
        if (message.Role != MessageRole.System || message.ContentBlocks.Count != 1)
        {
            return null;
        }

        var block = message.ContentBlocks[0];
        if (block.Kind != MessageContentKind.Progress ||
            block.Metadata is null ||
            !block.Metadata.TryGetValue("parentToolUseId", out var parentToolUseId) ||
            string.IsNullOrWhiteSpace(parentToolUseId) ||
            !block.Metadata.TryGetValue("toolUseId", out var toolUseId) ||
            string.IsNullOrWhiteSpace(toolUseId))
        {
            return null;
        }

        if (!toolNamesByToolUseId.TryGetValue(parentToolUseId, out var toolName) ||
            !_tools.TryResolve(toolName, out var tool) ||
            tool is null)
        {
            return null;
        }

        if (JsonNode.Parse(block.Value) is not JsonObject progressData)
        {
            return null;
        }

        if (!progressMessagesByParentToolUseId.TryGetValue(parentToolUseId, out var progressMessages))
        {
            progressMessages = [];
            progressMessagesByParentToolUseId[parentToolUseId] = progressMessages;
        }

        progressMessages.Add(new ToolProgressUpdate(toolUseId, progressData));
        var rendered = tool.RenderToolUseProgressMessage(progressMessages)
                       ?? _progressIndicatorRenderer.TryRender(progressData);
        return string.IsNullOrWhiteSpace(rendered)
            ? null
            : new RenderedToolProgress(parentToolUseId, rendered);
    }
}

public sealed record RenderedToolProgress(string ParentToolUseId, string Content);
