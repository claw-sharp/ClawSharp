// TS origin: ./utils/attachments.ts, ./utils/messages.ts
// TS parity status: ports the focused hook_permission_decision attachment payload used by the TypeScript hook/query path; live hook execution remains blocked on the missing model-backed query loop.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed record QueryHookPermissionDecisionAttachment(
    string Decision,
    string ToolUseId,
    HookEvent HookEvent);
