// TS parity status: ports the focused hook_additional_context attachment payload used by the TypeScript hook/query path; live hook execution remains blocked on the missing model-backed query loop.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed record QueryHookAdditionalContextAttachment(
    IReadOnlyList<string> Content,
    string HookName,
    string ToolUseId,
    HookEvent HookEvent);
