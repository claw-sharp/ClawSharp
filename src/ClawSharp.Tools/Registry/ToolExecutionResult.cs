using System.Text.Json.Nodes;

namespace ClawSharp.Tools;

public sealed record ToolExecutionResult(
    bool Success,
    string Output,
    JsonNode? StructuredOutput = null);
