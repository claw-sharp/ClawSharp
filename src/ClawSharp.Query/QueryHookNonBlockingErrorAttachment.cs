// TS parity status: ports the focused hook_non_blocking_error attachment payload used by the TypeScript hook/query path; live hook execution remains blocked on the missing streamed hook runtime.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed record QueryHookNonBlockingErrorAttachment(
    string HookName,
    string Stderr,
    string Stdout,
    int ExitCode,
    string ToolUseId,
    HookEvent HookEvent,
    string? Command = null,
    int? DurationMs = null);
