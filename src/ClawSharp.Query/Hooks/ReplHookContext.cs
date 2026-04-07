// TS parity status: ports the internal REPL hook context contract that TypeScript post-sampling hooks receive; current C# uses the closest currently available query-loop and prompt context shapes until live model-backed execution is ported.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed record ReplHookContext(
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<string> SystemPrompt,
    IReadOnlyDictionary<string, string> UserContext,
    IReadOnlyDictionary<string, string> SystemContext,
    QueryToolUseContextState ToolUseContext,
    string? QuerySource = null);
