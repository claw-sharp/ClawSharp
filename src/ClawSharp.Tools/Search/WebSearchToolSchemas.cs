using System.Text.Json.Nodes;

namespace ClawSharp.Tools;

internal static class WebSearchToolSchemas
{
    public static JsonObject InputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("query", ToolJsonSchemaFactory.String("The search query to use")),
                ("allowed_domains", ToolJsonSchemaFactory.Array(ToolJsonSchemaFactory.String(), "Only include search results from these domains")),
                ("blocked_domains", ToolJsonSchemaFactory.Array(ToolJsonSchemaFactory.String(), "Never include search results from these domains"))
            ],
            required: ["query"]);

    public static JsonObject SearchResultSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("tool_use_id", ToolJsonSchemaFactory.String("ID of the tool use")),
                ("content", ToolJsonSchemaFactory.Array(
                    ToolJsonSchemaFactory.StrictObject(
                        [
                            ("title", ToolJsonSchemaFactory.String("The title of the search result")),
                            ("url", ToolJsonSchemaFactory.String("The URL of the search result"))
                        ]
                    ),
                    "Array of search hits"
                ))
            ]);

    public static JsonObject OutputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("query", ToolJsonSchemaFactory.String("The search query that was executed")),
                ("results", ToolJsonSchemaFactory.Array(
                    ToolJsonSchemaFactory.AnyOf(
                        [
                            SearchResultSchema,
                            ToolJsonSchemaFactory.String()
                        ]
                    ),
                    "Search results and/or text commentary from the model"
                )),
                ("durationSeconds", ToolJsonSchemaFactory.Number("Time taken to complete the search operation"))
            ],
            required: ["query", "results", "durationSeconds"]);
}
