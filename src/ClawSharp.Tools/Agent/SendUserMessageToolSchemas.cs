using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Tools;

internal static class SendUserMessageToolSchemas
{
    public static readonly JsonObject InputSchema = (JsonObject)JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "message": {
              "type": "string",
              "description": "The message for the user. Supports markdown formatting."
            },
            "attachments": {
              "type": "array",
              "items": {
                "type": "string"
              },
              "description": "Optional file paths (absolute or relative to cwd) to attach. Use for photos, screenshots, diffs, logs, or any file the user should see alongside your message."
            },
            "status": {
              "type": "string",
              "enum": ["normal", "proactive"],
              "description": "Use 'proactive' when you're surfacing something the user hasn't asked for and needs to see now — task completion while they're away, a blocker you hit, an unsolicited status update. Use 'normal' when replying to something the user just said."
            }
          },
          "required": ["message", "status"],
          "additionalProperties": false
        }
        """)!;

    public static readonly JsonObject OutputSchema = (JsonObject)JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "message": {
              "type": "string",
              "description": "The message"
            },
            "attachments": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "path": { "type": "string" },
                  "size": { "type": "number" },
                  "isImage": { "type": "boolean" },
                  "file_uuid": { "type": "string" }
                },
                "required": ["path", "size", "isImage"]
              },
              "description": "Resolved attachment metadata"
            },
            "sentAt": {
              "type": "string",
              "description": "ISO timestamp captured at tool execution on the emitting process."
            }
          },
          "required": ["message"]
        }
        """)!;
}
