// TS origin: ./utils/attachments.ts, ./utils/messages.ts
// TS parity status: ports the focused hook_error_during_execution attachment payload used by the TypeScript hook/query path; live hook execution remains blocked on the missing streamed hook runtime.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed record QueryHookErrorDuringExecutionAttachment(
    string Content,
    string HookName,
    string ToolUseId,
    HookEvent HookEvent,
    string? Command = null,
    int? DurationMs = null);
