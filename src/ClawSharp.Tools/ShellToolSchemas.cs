// TS origin: ./tools/BashTool/BashTool.tsx, ./tools/PowerShellTool/PowerShellTool.tsx
using System.Text.Json.Nodes;

namespace ClawSharp.Tools;

internal static class ShellToolSchemas
{
    public static JsonObject CreateShellInputSchema(bool includeRunInBackground)
    {
        var properties = new List<(string Name, JsonNode Schema)>
        {
            ("command", ToolJsonSchemaFactory.String("The command to execute")),
            ("timeout", ToolJsonSchemaFactory.Number("Optional timeout in milliseconds", minimum: 0, maximum: 1800000)),
            ("description", ToolJsonSchemaFactory.String("Clear, concise description of what this command does")),
            ("dangerouslyDisableSandbox", ToolJsonSchemaFactory.Boolean("Set to true to disable sandboxing"))
        };

        if (includeRunInBackground)
        {
            properties.Add(("run_in_background", ToolJsonSchemaFactory.Boolean("Set to true to run this command in the background")));
        }

        return ToolJsonSchemaFactory.StrictObject(
            properties,
            required:
            [
                "command"
            ]);
    }

    public static readonly JsonObject ShellOutputSchema = ToolJsonSchemaFactory.StrictObject(
        [
            ("stdout", ToolJsonSchemaFactory.String("The standard output of the command")),
            ("stderr", ToolJsonSchemaFactory.String("The standard error output of the command")),
            ("interrupted", ToolJsonSchemaFactory.Boolean("Whether the command was interrupted")),
            ("backgroundTaskId", ToolJsonSchemaFactory.Nullable(ToolJsonSchemaFactory.String())),
            ("backgroundedByUser", ToolJsonSchemaFactory.Nullable(ToolJsonSchemaFactory.Boolean())),
            ("assistantAutoBackgrounded", ToolJsonSchemaFactory.Nullable(ToolJsonSchemaFactory.Boolean())),
            ("dangerouslyDisableSandbox", ToolJsonSchemaFactory.Nullable(ToolJsonSchemaFactory.Boolean())),
            ("returnCodeInterpretation", ToolJsonSchemaFactory.Nullable(ToolJsonSchemaFactory.String())),
            ("persistedOutputPath", ToolJsonSchemaFactory.Nullable(ToolJsonSchemaFactory.String())),
            ("persistedOutputSize", ToolJsonSchemaFactory.Nullable(ToolJsonSchemaFactory.Number())),
            ("isImage", ToolJsonSchemaFactory.Nullable(ToolJsonSchemaFactory.Boolean())),
            ("noOutputExpected", ToolJsonSchemaFactory.Nullable(ToolJsonSchemaFactory.Boolean()))
        ],
        required:
        [
            "stdout",
            "stderr",
            "interrupted"
        ]);
}
