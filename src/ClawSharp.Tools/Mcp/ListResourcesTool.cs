using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClawSharp.Tools.Mcp;

public sealed class ListMcpResourcesTool : BaseTool
{
    public const string ToolName = "ListMcpResources";

    public ListMcpResourcesTool()
        : base(new ToolDescriptor(
            ToolName,
            "Lists resources from connected MCP servers. Resources are typically static, read-only entities provided by a server, which the model can read using ReadMcpResource.",
            SearchHint: "list resources from connected MCP servers",
            ShouldDefer: true,
            InputSchema: ListResourcesToolSchemas.InputSchema,
            OutputSchema: ListResourcesToolSchemas.OutputSchema))
    {
    }

    public override bool IsConcurrencySafe(string arguments) => true;
    public override bool IsReadOnly(string arguments) => true;

    public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        string? targetServer = null;
        try
        {
            using var doc = JsonDocument.Parse(context.Arguments);
            if (doc.RootElement.TryGetProperty("server", out var serverProp))
            {
                targetServer = serverProp.GetString();
            }
        }
        catch
        {
            // Ignore parse errors, default to all servers
        }

        var resources = context.McpResources.GetResources(targetServer);
        
        var resultArr = new JsonArray();
        foreach (var resource in resources)
        {
            resultArr.Add(new JsonObject
            {
                ["uri"] = resource.Uri,
                ["name"] = resource.Name,
                ["mimeType"] = resource.MimeType,
                ["description"] = resource.Description,
                ["server"] = resource.Server
            });
        }

        if (resultArr.Count == 0)
        {
            return Task.FromResult(Success("No resources found. MCP servers may still provide tools even if they have no resources.", resultArr));
        }

        return Task.FromResult(Success(resultArr.ToJsonString(), resultArr));
    }
}
