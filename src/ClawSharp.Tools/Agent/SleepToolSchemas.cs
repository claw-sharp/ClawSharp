using System.Text.Json.Nodes;

namespace ClawSharp.Tools;

internal static class SleepToolSchemas
{
    public static readonly JsonObject InputSchema = (JsonObject)JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "duration_ms": {
              "type": "number",
              "minimum": 0,
              "maximum": 300000,
              "description": "Duration to sleep in milliseconds"
            },
            "reason": {
              "type": "string",
              "description": "Reason for sleeping"
            }
          },
          "additionalProperties": false
        }
        """)!;

    public static readonly JsonObject OutputSchema = (JsonObject)JsonNode.Parse("""
        {
          "type": "string",
          "description": "Sleep result"
        }
        """)!;
}
