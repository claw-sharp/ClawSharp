// TS parity status: simplified foundation only, not a 1:1 translation yet.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed record QueryResult(
    ChatMessage? AssistantMessage,
    bool UsedTool,
    IReadOnlyList<string> ToolNames,
    IReadOnlyList<string> StreamedChunks,
    QueryLoopTerminal Terminal,
    QueryLoopState State,
    QueryModelRequest? Request = null);
