// TS origin: ./types/hooks.ts, ./utils/hooks.ts
// TS parity status: ports the focused HookBlockingError payload used by hook_blocking_error attachments; live hook execution remains blocked on the missing model-backed query loop.
namespace ClawSharp.Core;

public sealed record HookBlockingError(
    string BlockingError,
    string Command);
