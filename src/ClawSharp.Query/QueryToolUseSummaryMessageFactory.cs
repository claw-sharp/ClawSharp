// TS origin: ./utils/messages.ts
// TS parity status: ports the pure TypeScript createToolUseSummaryMessage helper without inventing broader query emission behavior.
namespace ClawSharp.Query;

public static class QueryToolUseSummaryMessageFactory
{
    public static QueryToolUseSummaryMessage Create(
        string summary,
        IReadOnlyList<string> precedingToolUseIds)
    {
        return new QueryToolUseSummaryMessage(
            summary,
            precedingToolUseIds.ToArray(),
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow);
    }
}
