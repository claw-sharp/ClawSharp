using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClawSharp.Tools.REPL;

internal sealed class REPLTool : BaseTool
{
    public REPLTool() : base(new ToolDescriptor(
        Name: "REPL",
        Description: "A batch execution tool that allows running multiple primitive actions (FileRead, FileWrite, Bash, etc.) in a single call. Use this to perform complex sequences of operations efficiently.",
        InputSchema: new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["actions"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["tool"] = new JsonObject { ["type"] = "string" },
                            ["input"] = new JsonObject
                            {
                                ["description"] = "Arguments for the nested tool. Provide either a raw JSON object or a JSON-encoded string when the provider requires strict schemas.",
                                ["anyOf"] = new JsonArray
                                {
                                    new JsonObject
                                    {
                                        ["type"] = "object"
                                    },
                                    new JsonObject
                                    {
                                        ["type"] = "string",
                                        ["description"] = "JSON-encoded arguments for the nested tool."
                                    }
                                }
                            }
                        },
                        ["required"] = new JsonArray { "tool", "input" }
                    },
                    ["description"] = "A list of tool actions to perform sequentially."
                }
            },
            ["required"] = new JsonArray { "actions" }
        }))
    {
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var inputJson = context.Arguments;
        var input = JsonSerializer.Deserialize<REPLInput>(inputJson);
        if (input == null) return Failure("Invalid input");

        var results = new JsonArray();
        var registry = context.ToolRegistry;
        if (registry == null) return Failure("Tool registry not available in context.");

        foreach (var action in input.actions)
        {
            if (string.IsNullOrEmpty(action.tool)) continue;

            if (registry.TryResolve(action.tool, out var tool) && tool != null)
            {
                try
                {
                    string actionInputJson = SerializeActionInput(action.input);
                    var subContext = context with { Arguments = actionInputJson };
                    
                    var result = await tool.ExecuteAsync(subContext, cancellationToken);
                    
                    results.Add(new JsonObject
                    {
                        ["tool"] = action.tool,
                        ["success"] = result.Success,
                        ["output"] = result.Output,
                        ["structured_output"] = result.StructuredOutput?.DeepClone()
                    });
                }
                catch (Exception ex)
                {
                    results.Add(new JsonObject
                    {
                        ["tool"] = action.tool,
                        ["success"] = false,
                        ["error"] = ex.Message
                    });
                }
            }
            else
            {
                results.Add(new JsonObject
                {
                    ["tool"] = action.tool,
                    ["success"] = false,
                    ["error"] = $"Tool \"{action.tool}\" not found or not accessible via REPL."
                });
            }
        }

        return Success("Executed batch actions.", results);
    }

    private class REPLInput
    {
        public List<ToolAction> actions { get; set; } = new();
    }

    private class ToolAction
    {
        public string tool { get; set; } = string.Empty;
        public JsonElement input { get; set; }
    }

    private static string SerializeActionInput(JsonElement input)
    {
        if (input.ValueKind == JsonValueKind.String)
        {
            var encodedJson = input.GetString();
            if (string.IsNullOrWhiteSpace(encodedJson))
            {
                return "{}";
            }

            try
            {
                return JsonNode.Parse(encodedJson)!.ToJsonString();
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("REPL action input string must contain valid JSON.", ex);
            }
        }

        if (input.ValueKind == JsonValueKind.Undefined)
        {
            return "{}";
        }

        return input.GetRawText();
    }
}
