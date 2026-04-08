using System.Text.Json.Nodes;

namespace ClawSharp.Tools;

internal static class TaskCreateToolSchemas
{
    public static JsonObject InputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("subject", ToolJsonSchemaFactory.String("A brief title for the task")),
                ("description", ToolJsonSchemaFactory.String("What needs to be done")),
                ("activeForm", ToolJsonSchemaFactory.String("Present continuous form shown in spinner when in_progress (e.g., \"Running tests\")", Required: false)),
                ("metadata", ToolJsonSchemaFactory.Object(Required: false, description: "Arbitrary metadata to attach to the task"))
            ],
            required:
            [
                "subject",
                "description"
            ]);

    public static JsonObject OutputSchema =>
        ToolJsonSchemaFactory.Object(
            [
                ("task", ToolJsonSchemaFactory.Object([
                    ("id", ToolJsonSchemaFactory.String("Assigned task ID")),
                    ("subject", ToolJsonSchemaFactory.String("Task title"))
                ], required: ["id", "subject"]))
            ],
            required:
            [
                "task"
            ]);
}
