using System.Text.Json.Nodes;

namespace ClawSharp.Tools;

internal sealed class WebSearchTool : BaseTool
{
    public WebSearchTool()
        : base(
            new ToolDescriptor(
                "WebSearch",
                "Google search for content on the web",
                SearchHint: "Google search for content on the web",
                InputSchema: WebSearchToolSchemas.InputSchema,
                OutputSchema: WebSearchToolSchemas.OutputSchema,
                Strict: true))
    {
    }

    public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var root = JsonNode.Parse(context.Arguments)?.AsObject();
        var query = root?["query"]?.ToString() ?? string.Empty;

        // In ClawSharp, we are blocked on a specific Google/Tavily/etc. decision.
        // Parity note: Source delegates to api.anthropic.com/beta/web_search_20250305.
        // We return a message indicating that the search should be performed by the model if it supports it, 
        // or that it is currently not available in this build.
        return Task.FromResult(Failure(
            $"Web search for '{query}' is not yet configured for a specific provider in this ClawSharp build. Please use WebFetch if you have a specific URL to visit."));
    }
}
