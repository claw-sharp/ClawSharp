// TS origin: ./query.ts
namespace ClawSharp.Query;

public enum QueryTerminalReason
{
    Completed,
    BlockingLimit,
    ImageError,
    ModelError,
    AbortedStreaming,
    PromptTooLong,
    StopHookPrevented,
    AbortedTools,
    HookStopped,
    MaxTurns
}
