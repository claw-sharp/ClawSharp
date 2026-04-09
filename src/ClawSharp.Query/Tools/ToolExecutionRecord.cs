// TS parity status: simplified foundation only, not a 1:1 translation yet.
using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed record ToolExecutionRecord(
    ToolCallRequest ToolCall,
    bool Success,
    string Output,
    JsonNode? StructuredOutput,
    IReadOnlyList<ChatMessage>? InjectedMessages = null);
