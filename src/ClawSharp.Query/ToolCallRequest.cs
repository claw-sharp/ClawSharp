// TS parity status: simplified foundation only, not a 1:1 translation yet.
namespace ClawSharp.Query;

public sealed record ToolCallRequest(
    string ToolUseId,
    string ToolName,
    string Arguments);
