// TS origin: ./utils/attachments.ts, ./utils/messages.ts
// TS parity status: ports the focused hook_cancelled attachment payload used by the TypeScript hook/query path; live hook execution remains blocked on the missing model-backed query loop.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed record QueryHookCancelledAttachment(
    string HookName,
    string ToolUseId,
    HookEvent HookEvent,
    string? Command = null,
    int? DurationMs = null);
