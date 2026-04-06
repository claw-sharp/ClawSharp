// TS origin: ./services/compact/compact.ts
// TS parity status: ports the current TypeScript compaction-result message contract and preserves the post-compact message ordering inputs, including the raw compact summary text needed by the post-compact hook phase; compaction-specific analytics payloads remain intentionally unported until the concrete compaction runtime exists.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed record QueryCompactionResult(
    ChatMessage BoundaryMarker,
    IReadOnlyList<ChatMessage> SummaryMessages,
    IReadOnlyList<ChatMessage> Attachments,
    IReadOnlyList<ChatMessage> HookResults,
    IReadOnlyList<ChatMessage>? MessagesToKeep = null,
    string? RawSummary = null,
    string? UserDisplayMessage = null,
    int? PreCompactTokenCount = null,
    int? PostCompactTokenCount = null,
    int? TruePostCompactTokenCount = null);
