using System.Text.Json.Nodes;
using ClawSharp.Tools.Registry;

namespace ClawSharp.Tools.Mcp;

internal static class ReadResourceToolSchemas
{
    public static readonly JsonObject InputSchema = ToolJsonSchemaFactory.StrictObject(
        new[]
        {
            ("server", ToolJsonSchemaFactory.String("The MCP server name")),
            ("uri", ToolJsonSchemaFactory.String("The resource URI to read"))
        },
        required: new[] { "server", "uri" });

    public static readonly JsonObject OutputSchema = ToolJsonSchemaFactory.StrictObject(
        new[]
        {
            ("contents", ToolJsonSchemaFactory.Array(
                ToolJsonSchemaFactory.StrictObject(
                    new[]
                    {
                        ("uri", ToolJsonSchemaFactory.String("Resource URI")),
                        ("mimeType", ToolJsonSchemaFactory.String("MIME type of the content")),
                        ("text", ToolJsonSchemaFactory.String("Text content of the resource")),
                        ("blobSavedTo", ToolJsonSchemaFactory.String("Path where binary blob content was saved"))
                    },
                    required: new[] { "uri" })))
        });
}
