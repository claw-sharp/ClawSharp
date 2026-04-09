using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ClawSharp.Core;

namespace ClawSharp.Tools.Search;

internal sealed class ToolSearchTool : BaseTool
{
    public ToolSearchTool()
        : base(
            new ToolDescriptor(
                "ToolSearch",
                "Find deferred tools by keywords or direct selection",
                Parameters:
                [
                    new ToolParameter("query", "Query phrase or select:<name>"),
                    new ToolParameter("max_results", "Max matches to return (default 5)")
                ],
                InputSchema: ToolSearchToolSchemas.InputSchema,
                OutputSchema: ToolSearchToolSchemas.OutputSchema,
                SearchHint: "find and activate specialized tools on demand",
                Strict: true))
    {
    }

    public override Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var arguments = context.Arguments;
        var query = string.Empty;
        var maxResults = 5;

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var doc = JsonDocument.Parse(arguments);
            if (doc.RootElement.TryGetProperty("query", out var q)) query = q.GetString() ?? string.Empty;
            if (doc.RootElement.TryGetProperty("max_results", out var m) && m.TryGetInt32(out var res)) maxResults = res;
        }
        catch { }

        if (string.IsNullOrWhiteSpace(query)) return Task.FromResult(Failure("Query is required."));

        var allAvailableTools = context.AvailableTools ?? [];
        var deferredTools = allAvailableTools.Where(t => t.ShouldDefer).ToList();
        
        var matches = new List<string>();

        // Select: prefix
        if (query.StartsWith("select:", StringComparison.OrdinalIgnoreCase))
        {
            var requestedNames = query.Substring(7).Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s));
            foreach (var requestedName in requestedNames)
            {
                var tool = allAvailableTools.FirstOrDefault(t => string.Equals(t.Name, requestedName, StringComparison.OrdinalIgnoreCase));
                if (tool != null && !matches.Contains(tool.Name)) matches.Add(tool.Name);
            }
        }
        else
        {
            // Keyword search
            var searchTerms = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var scored = new List<(string Name, int Score)>();

            foreach (var tool in deferredTools)
            {
                var score = 0;
                var toolText = (tool.Name + " " + (tool.SearchHint ?? "") + " " + tool.Description).ToLowerInvariant();

                foreach (var term in searchTerms)
                {
                    if (tool.Name.Equals(term, StringComparison.OrdinalIgnoreCase)) score += 10;
                    else if (tool.Name.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 5;
                    
                    if (tool.SearchHint?.Contains(term, StringComparison.OrdinalIgnoreCase) == true) score += 3;
                    if (tool.Description.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 1;
                }

                if (score > 0) scored.Add((tool.Name, score));
            }

            matches = scored.OrderByDescending(s => s.Score).Take(maxResults).Select(s => s.Name).ToList();
        }

        var result = new JsonObject
        {
            ["matches"] = new JsonArray(matches.Select(m => (JsonNode)m!).ToArray()),
            ["query"] = query,
            ["total_deferred_tools"] = deferredTools.Count
        };

        var content = matches.Count == 0 ? "No matching deferred tools found." : $"Found {matches.Count} matching tools.";
        return Task.FromResult(Success(content, result));
    }
}
