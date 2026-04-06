// TS origin: ./utils/attachments.ts, ./utils/messages.ts
// TS parity status: ports the focused hook_success attachment payload used by the TypeScript hook/query path; live hook execution remains blocked on the missing model-backed query loop.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed record QueryHookSuccessAttachment(
    string Content,
    string HookName,
    string ToolUseId,
    HookEvent HookEvent,
    string? Stdout = null,
    string? Stderr = null,
    int? ExitCode = null,
    string? Command = null,
    int? DurationMs = null);
