using System.Text.Json.Nodes;

namespace ClawSharp.Tools;

public static class WebFetchToolSchemas
{
    public static readonly JsonObject InputSchema = new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["url"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "The URL to fetch content from"
            },
            ["prompt"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "The prompt to run on the fetched content"
            }
        },
        ["required"] = new JsonArray { "url", "prompt" },
        ["additionalProperties"] = false
    };

    public static readonly JsonObject OutputSchema = new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["bytes"] = new JsonObject
            {
                ["type"] = "number",
                ["description"] = "Size of the fetched content in bytes"
            },
            ["code"] = new JsonObject
            {
                ["type"] = "number",
                ["description"] = "HTTP response code"
            },
            ["codeText"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "HTTP response code text"
            },
            ["result"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "Processed result from applying the prompt to the content"
            },
            ["durationMs"] = new JsonObject
            {
                ["type"] = "number",
                ["description"] = "Time taken to fetch and process the content"
            },
            ["url"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "The URL that was fetched"
            }
        }
    };
}
