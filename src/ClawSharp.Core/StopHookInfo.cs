// TS origin: ./types/message.ts
// TS parity status: ports the TypeScript stop-hook info payload used by stop_hook_summary system messages; the surrounding stop-hook runtime remains unported.
namespace ClawSharp.Core;

public sealed record StopHookInfo(
    string Command,
    string? PromptText = null);
