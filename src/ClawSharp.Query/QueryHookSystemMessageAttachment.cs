// TS parity status: ports the focused hook_system_message attachment payload used by the TypeScript hook/query path; live hook execution remains blocked on the missing model-backed query loop.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed record QueryHookSystemMessageAttachment(
    string Content,
    string HookName,
    string ToolUseId,
    HookEvent HookEvent);
