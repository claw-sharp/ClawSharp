using System.Text.Json.Nodes;

namespace ClawSharp.Tools.Search;

internal static class ToolSearchToolSchemas
{
    public static JsonObject InputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("query", ToolJsonSchemaFactory.String("Query to find deferred tools. Use \"select:<tool_name>\" for direct selection, or keywords to search.")),
                ("max_results", ToolJsonSchemaFactory.Number("Maximum number of results to return (default: 5)", minimum: 1, maximum: 20, defaultValue: 5))
            ],
            required: ["query"]);

    public static JsonObject OutputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("matches", ToolJsonSchemaFactory.Array(ToolJsonSchemaFactory.String(), "List of matching tool names")),
                ("query", ToolJsonSchemaFactory.String("The original query")),
                ("total_deferred_tools", ToolJsonSchemaFactory.Number("Total number of deferred tools available"))
            ],
            required: ["matches", "query", "total_deferred_tools"]);
}
