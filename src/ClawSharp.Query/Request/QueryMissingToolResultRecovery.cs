// TS parity status: ports the synthetic missing-tool_result recovery helper used when a turn emitted tool_use blocks before failing; broader API-error message recovery remains blocked on the missing model-backed loop.
using ClawSharp.Core;

namespace ClawSharp.Query;

public static class QueryMissingToolResultRecovery
{
    public static IReadOnlyList<ChatMessage> CreateMissingToolResultMessagesForUnmatchedToolUses(
        IReadOnlyList<ChatMessage> messages,
        string errorMessage)
    {
        HashSet<string> satisfiedToolUseIds = new(StringComparer.Ordinal);
        List<(string ToolUseId, string ToolName)> missingToolUses = [];

        foreach (var message in messages.Where(message => message.Role == MessageRole.User))
        {
            foreach (var toolResultBlock in message.ContentBlocks.Where(block => block.Kind == MessageContentKind.ToolResult))
            {
                if (toolResultBlock.Metadata is not null &&
                    toolResultBlock.Metadata.TryGetValue("toolUseId", out var toolUseId) &&
                    !string.IsNullOrWhiteSpace(toolUseId))
                {
                    satisfiedToolUseIds.Add(toolUseId);
                }
            }
        }

        foreach (var message in messages)
        {
            if (message.Role != MessageRole.Assistant)
            {
                continue;
            }

            foreach (var toolUseBlock in message.ContentBlocks.Where(block => block.Kind == MessageContentKind.ToolUse))
            {
                if (toolUseBlock.Metadata is null ||
                    !toolUseBlock.Metadata.TryGetValue("toolUseId", out var toolUseId) ||
                    string.IsNullOrWhiteSpace(toolUseId) ||
                    satisfiedToolUseIds.Contains(toolUseId))
                {
                    continue;
                }

                missingToolUses.Add((toolUseId, toolUseBlock.Name ?? string.Empty));
            }
        }

        return missingToolUses
            .Select(toolUse => ChatMessageFactory.CreateToolResult(toolUse.ToolUseId, toolUse.ToolName, errorMessage))
            .ToArray();
    }

    public static IReadOnlyList<ChatMessage> CreateMissingToolResultMessages(
        IReadOnlyList<ChatMessage> assistantMessages,
        string errorMessage)
    {
        List<ChatMessage> recoveredMessages = [];

        foreach (var assistantMessage in assistantMessages)
        {
            foreach (var toolUseBlock in assistantMessage.ContentBlocks.Where(block => block.Kind == MessageContentKind.ToolUse))
            {
                if (toolUseBlock.Metadata is null ||
                    !toolUseBlock.Metadata.TryGetValue("toolUseId", out var toolUseId) ||
                    string.IsNullOrWhiteSpace(toolUseId))
                {
                    continue;
                }

                recoveredMessages.Add(
                    ChatMessageFactory.CreateToolResult(
                        toolUseId,
                        toolUseBlock.Name ?? string.Empty,
                        errorMessage));
            }
        }

        return recoveredMessages;
    }
}
