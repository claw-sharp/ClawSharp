// TS parity status: ports the focused hook_blocking_error attachment payload used by the TypeScript hook/query path; live hook execution remains blocked on the missing model-backed query loop.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed record QueryHookBlockingErrorAttachment(
    HookBlockingError BlockingError,
    string HookName,
    string ToolUseId,
    HookEvent HookEvent);
