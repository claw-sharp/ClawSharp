using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Tools;

public sealed record ToolExecutionResult(
    bool Success,
    string Output,
    JsonNode? StructuredOutput = null,
    IReadOnlyList<ChatMessage>? InjectedMessages = null);
