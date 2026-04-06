// TS parity status: ports the current TypeScript streamed tool-update surface as a C# query-runtime contract; mutable tool context updates remain blocked on the missing model-backed runtime/tool context.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed record ToolExecutionUpdate(
    ChatMessage? Message = null,
    ToolExecutionRecord? Result = null,
    QueryToolUseContextState? UpdatedToolUseContext = null);
