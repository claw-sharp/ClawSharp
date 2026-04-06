// TS origin: ./services/compact/compact.ts
// TS parity status: ports the current TypeScript buildPostCompactMessages ordering exactly for the C# query-loop recovery path.
using ClawSharp.Core;

namespace ClawSharp.Query;

public static class QueryPostCompactMessageBuilder
{
    public static IReadOnlyList<ChatMessage> BuildPostCompactMessages(QueryCompactionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return
        [
            result.BoundaryMarker,
            .. result.SummaryMessages,
            .. (result.MessagesToKeep ?? []),
            .. result.Attachments,
            .. result.HookResults
        ];
    }
}
