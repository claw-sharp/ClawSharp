using System.Text.Json.Nodes;

namespace ClawSharp.Tools.Plan;

internal static class PlanToolSchemas
{
    public static JsonObject EnterInputSchema =>
        ToolJsonSchemaFactory.StrictObject(Array.Empty<(string Name, JsonNode Schema)>(), required: []);

    public static JsonObject EnterOutputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("message", ToolJsonSchemaFactory.String("Confirmation message"))
            ],
            required: ["message"]);

    public static JsonObject ExitInputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("plan", ToolJsonSchemaFactory.String("The final plan or summary of work done"))
            ],
            required: ["plan"]);

    public static JsonObject ExitOutputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("message", ToolJsonSchemaFactory.String("Confirmation message"))
            ],
            required: ["message"]);

    public static JsonObject VerifyInputSchema =>
        ToolJsonSchemaFactory.StrictObject(Array.Empty<(string Name, JsonNode Schema)>(), required: []);

    public static JsonObject VerifyOutputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("message", ToolJsonSchemaFactory.String("Verification status summary")),
                ("plan", ToolJsonSchemaFactory.String("The plan being verified")),
                ("verificationStarted", ToolJsonSchemaFactory.Boolean("Whether verification has started")),
                ("verificationCompleted", ToolJsonSchemaFactory.Boolean("Whether verification has completed"))
            ],
            required: ["message", "plan", "verificationStarted", "verificationCompleted"]);
}
