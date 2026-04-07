using System.Text.Json.Nodes;

namespace ClawSharp.Tools;

internal static class TodoWriteToolSchemas
{
    public static readonly JsonNode TodoItemSchema = JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "content": { "type": "string", "minLength": 1 },
            "status": { "type": "string", "enum": ["pending", "in_progress", "completed"] },
            "activeForm": { "type": "string", "minLength": 1 }
          },
          "required": ["content", "status", "activeForm"],
          "additionalProperties": false
        }
        """)!;

    public static readonly JsonNode InputSchema = JsonNode.Parse($$"""
        {
          "type": "object",
          "properties": {
            "todos": {
              "type": "array",
              "items": {{TodoItemSchema.ToJsonString()}},
              "description": "The updated todo list"
            }
          },
          "required": ["todos"],
          "additionalProperties": false
        }
        """)!;

    public static readonly JsonNode OutputSchema = JsonNode.Parse($$"""
        {
          "type": "object",
          "properties": {
            "oldTodos": { "type": "array", "items": {{TodoItemSchema.ToJsonString()}} },
            "newTodos": { "type": "array", "items": {{TodoItemSchema.ToJsonString()}} },
            "verificationNudgeNeeded": { "type": "boolean" }
          },
          "required": ["oldTodos", "newTodos"]
        }
        """)!;
}
