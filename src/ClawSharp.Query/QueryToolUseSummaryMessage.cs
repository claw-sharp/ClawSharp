// TS origin: ./utils/messages.ts, ./query.ts
// TS parity status: ports the TypeScript tool_use_summary message contract carried by the query loop; live runtime emission is still blocked on the missing model-backed loop.
namespace ClawSharp.Query;

public sealed record QueryToolUseSummaryMessage(
    string Summary,
    IReadOnlyList<string> PrecedingToolUseIds,
    string Id,
    DateTimeOffset Timestamp);
