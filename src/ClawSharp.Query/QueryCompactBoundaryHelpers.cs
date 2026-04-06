// TS origin: ./utils/messages.ts
// TS parity status: ports the pure TypeScript compact-boundary detection and slicing helpers without inventing broader compaction control flow.
using ClawSharp.Core;

namespace ClawSharp.Query;

public static class QueryCompactBoundaryHelpers
{
    public static bool IsCompactBoundaryMessage(ChatMessage? message)
    {
        if (message is null || message.Role != MessageRole.System || message.ContentBlocks.Count == 0)
        {
            return false;
        }

        return message.ContentBlocks.Any(
            block =>
                block.Kind == MessageContentKind.Text &&
                block.Metadata is not null &&
                block.Metadata.TryGetValue("subtype", out var subtype) &&
                string.Equals(subtype, "compact_boundary", StringComparison.Ordinal));
    }

    public static int FindLastCompactBoundaryIndex(IReadOnlyList<ChatMessage> messages)
    {
        for (var index = messages.Count - 1; index >= 0; index--)
        {
            if (IsCompactBoundaryMessage(messages[index]))
            {
                return index;
            }
        }

        return -1;
    }

    public static IReadOnlyList<ChatMessage> GetMessagesAfterCompactBoundary(IReadOnlyList<ChatMessage> messages)
    {
        var boundaryIndex = FindLastCompactBoundaryIndex(messages);
        return boundaryIndex == -1
            ? messages.ToArray()
            : messages.Skip(boundaryIndex).ToArray();
    }
}
