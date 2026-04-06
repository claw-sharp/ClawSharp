// TS origin: ./query/stopHooks.ts, ./utils/attachments.ts
// TS parity status: ports the focused hook_stopped_continuation attachment payload used by stop-hook and teammate-hook query paths; live hook execution remains blocked on the missing model-backed query loop.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed record QueryHookStoppedContinuationAttachment(
    string Message,
    string HookName,
    string ToolUseId,
    HookEvent HookEvent);
