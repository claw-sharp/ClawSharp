using System.Text.Json.Nodes;
using ClawSharp.Tools.Registry;

namespace ClawSharp.Tools.Mcp;

internal static class ListResourcesToolSchemas
{
    public static readonly JsonObject InputSchema = ToolJsonSchemaFactory.Object(
        new[]
        {
            ("server", ToolJsonSchemaFactory.String("Optional server name to filter resources by"))
        });

    public static readonly JsonObject OutputSchema = ToolJsonSchemaFactory.Array(
        ToolJsonSchemaFactory.StrictObject(
            new[]
            {
                ("uri", ToolJsonSchemaFactory.String("Resource URI")),
                ("name", ToolJsonSchemaFactory.String("Resource name")),
                ("mimeType", ToolJsonSchemaFactory.String("MIME type of the resource")),
                ("description", ToolJsonSchemaFactory.String("Resource description")),
                ("server", ToolJsonSchemaFactory.String("Server that provides this resource"))
            },
            required: new[] { "uri", "name", "server" }));
}
