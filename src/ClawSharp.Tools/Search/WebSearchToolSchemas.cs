using System.Text.Json.Nodes;

namespace ClawSharp.Tools;

public static class WebSearchToolSchemas
{
    public static readonly JsonObject InputSchema = new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["query"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "The search query"
            }
        },
        ["required"] = new JsonArray { "query" },
        ["additionalProperties"] = false
    };

    public static readonly JsonObject OutputSchema = new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["results"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["title"] = new JsonObject { ["type"] = "string" },
                        ["url"] = new JsonObject { ["type"] = "string" },
                        ["snippet"] = new JsonObject { ["type"] = "string" }
                    }
                }
            }
        }
    };
}
