using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Tools;

internal sealed class StructuredOutputTool : BaseTool
{
    private static readonly JsonNode StaticInputSchema = JsonNode.Parse("""
        {
          "type": "object",
          "additionalProperties": true
        }
        """)!;

    private static readonly JsonNode StaticOutputSchema = JsonNode.Parse("""
        {
          "type": "string",
          "description": "Structured output tool result"
        }
        """)!;

    public StructuredOutputTool()
        : base(new ToolDescriptor(
            "StructuredOutput",
            "Return the final response as structured JSON",
            Parameters: [],
            InputSchema: StaticInputSchema,
            OutputSchema: StaticOutputSchema,
            Strict: false,
            SearchHint: "return the final response as structured JSON"))
    {
    }

    public override bool IsConcurrencySafe(string arguments) => true;
    public override bool IsReadOnly(string arguments) => true;

    public override string? RenderToolUseMessage(string arguments)
    {
        try
        {
            var node = JsonNode.Parse(arguments);
            if (node is JsonObject obj)
            {
                var keys = obj.Select(kv => kv.Key).ToArray();
                if (keys.Length == 0) return null;
                if (keys.Length <= 3) return string.Join(", ", keys.Select(k => $"{k}: {obj[k]?.ToJsonString()}"));
                return $"{keys.Length} fields: {string.Join(", ", keys.Take(3))}\u2026";
            }
        }
        catch { }
        return null;
    }

    public override string? RenderToolResultMessage(string content, JsonNode? structuredOutput, IReadOnlyList<ToolProgressUpdate> progressMessages) => content;

    public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var input = JsonNode.Parse(context.Arguments);
        return Task.FromResult(Success("Structured output provided successfully", input));
    }
}
