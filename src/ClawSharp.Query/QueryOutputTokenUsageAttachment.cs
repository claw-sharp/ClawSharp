// TS origin: ./utils/attachments.ts, ./utils/messages.ts
// TS parity status: ports the focused output_token_usage attachment payload used by the TypeScript query/UI path; live token-budget attachment emission remains blocked on the missing model-backed query loop.
namespace ClawSharp.Query;

public sealed record QueryOutputTokenUsageAttachment(
    int Turn,
    int Session,
    int? Budget);
