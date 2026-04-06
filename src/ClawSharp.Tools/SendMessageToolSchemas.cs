// TS origin: ./tools/SendMessageTool/SendMessageTool.ts, ./tools/SendMessageTool/constants.ts
using System.Text.Json.Nodes;

namespace ClawSharp.Tools;

internal static class SendMessageToolSchemas
{
    public static readonly JsonObject InputSchema = ToolJsonSchemaFactory.StrictObject(
        [
            ("to", ToolJsonSchemaFactory.String("Recipient: teammate name, \"*\" for broadcast to all teammates, or a local agent id/name.")),
            ("summary", ToolJsonSchemaFactory.String("A 5-10 word summary shown as a preview in the UI (required when message is a string)")),
            ("message", CreateMessageSchema())
        ],
        required:
        [
            "to",
            "message"
        ]);

    public static readonly JsonObject OutputSchema = ToolJsonSchemaFactory.StrictObject(
        [
            ("success", ToolJsonSchemaFactory.Boolean()),
            ("message", ToolJsonSchemaFactory.String())
        ],
        required:
        [
            "success",
            "message"
        ]);

    private static JsonObject CreateMessageSchema()
    {
        return ToolJsonSchemaFactory.OneOf(
            ToolJsonSchemaFactory.String("Plain text message content"),
            ToolJsonSchemaFactory.StrictObject(
                [
                    ("type", ToolJsonSchemaFactory.StringEnum(["shutdown_request"])),
                    ("reason", ToolJsonSchemaFactory.String())
                ],
                required:
                [
                    "type"
                ]),
            ToolJsonSchemaFactory.StrictObject(
                [
                    ("type", ToolJsonSchemaFactory.StringEnum(["shutdown_response"])),
                    ("request_id", ToolJsonSchemaFactory.String()),
                    ("approve", ToolJsonSchemaFactory.Boolean()),
                    ("reason", ToolJsonSchemaFactory.String())
                ],
                required:
                [
                    "type",
                    "request_id",
                    "approve"
                ]),
            ToolJsonSchemaFactory.StrictObject(
                [
                    ("type", ToolJsonSchemaFactory.StringEnum(["plan_approval_response"])),
                    ("request_id", ToolJsonSchemaFactory.String()),
                    ("approve", ToolJsonSchemaFactory.Boolean()),
                    ("feedback", ToolJsonSchemaFactory.String())
                ],
                required:
                [
                    "type",
                    "request_id",
                    "approve"
                ]));
    }
}
