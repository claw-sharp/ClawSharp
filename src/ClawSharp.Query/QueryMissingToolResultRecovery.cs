// TS parity status: ports the synthetic missing-tool_result recovery helper used when a turn emitted tool_use blocks before failing; broader API-error message recovery remains blocked on the missing model-backed loop.
using ClawSharp.Core;

namespace ClawSharp.Query;

public static class QueryMissingToolResultRecovery
{
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
