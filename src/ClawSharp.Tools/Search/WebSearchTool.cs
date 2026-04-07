using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClawSharp.Core;
using HtmlAgilityPack;

namespace ClawSharp.Tools;

internal sealed class WebSearchTool : BaseTool
{
    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 10
    });

    private readonly INativeWebSearchService _nativeWebSearchService;

    public WebSearchTool(INativeWebSearchService? nativeWebSearchService = null)
        : base(
            new ToolDescriptor(
                "WebSearch",
                "Search the web for current information",
                SearchHint: "search the web for current information",
                InputSchema: WebSearchToolSchemas.InputSchema,
                OutputSchema: WebSearchToolSchemas.OutputSchema,
                Strict: true))
    {
        _nativeWebSearchService = nativeWebSearchService ?? new NullNativeWebSearchService();
    }

    public override bool IsEnabled()
    {
        if (IsFirecrawlEnabled()) return true;
        if (ShouldUseDuckDuckGo()) return true;

        var provider = ProviderRuntimeResolver.GetProvider();
        if (IsCodexResponsesWebSearchEnabled(provider)) return true;

        // Enable for Anthropic (first-party), Vertex, and Foundry
        if (provider is ApiProviderKind.Anthropic or ApiProviderKind.Vertex or ApiProviderKind.Foundry)
        {
            return true;
        }

        return false;
    }

    private static bool IsFirecrawlEnabled() => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FIRECRAWL_API_KEY"));

    private static bool ShouldUseFirecrawl()
    {
        if (!IsFirecrawlEnabled()) return false;
        var provider = ProviderRuntimeResolver.GetProvider();
        if (IsCodexResponsesWebSearchEnabled(provider)) return false;
        if (provider is ApiProviderKind.Anthropic or ApiProviderKind.Vertex or ApiProviderKind.Foundry) return false;
        return true;
    }

    private bool ShouldUseDuckDuckGo()
    {
        var provider = ProviderRuntimeResolver.GetProvider();
        if (IsCodexResponsesWebSearchEnabled(provider)) return false;

        // Don't override providers/models that have native web search support.
        if (provider is ApiProviderKind.Anthropic or ApiProviderKind.Vertex or ApiProviderKind.Foundry)
        {
            return false;
        }

        // Parity: Use free DDG search for non-Claude models by default.
        // Since IsEnabled() lacks the runtime context to verify context.AppState.MainLoopModel,
        // we return true as a candidate here and perform the precise model-brand check 
        // during ExecuteAsync.
        return true;
    }

    private static bool IsCodexResponsesWebSearchEnabled(ApiProviderKind provider)
    {
        if (provider != ApiProviderKind.OpenAi && provider != ApiProviderKind.Codex) return false;
        
        var (apiKey, _, _) = ProviderRuntimeResolver.ResolveCodexCredentials();
        return !string.IsNullOrEmpty(apiKey);
    }

    private static bool IsClaudeModel(string? model)
    {
        if (string.IsNullOrEmpty(model)) return true; // Default to true for safety
        return model.Contains("claude", StringComparison.OrdinalIgnoreCase);
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!TryParseInput(context.Arguments, out var input, out var error))
        {
            return Failure(error ?? "Invalid input.");
        }

        if (ShouldUseFirecrawl())
        {
            return await RunFirecrawlSearchAsync(input, context, cancellationToken);
        }

        if (ShouldUseDuckDuckGoSearch(context))
        {
            return await RunDuckDuckGoSearchAsync(input, context, cancellationToken);
        }

        var provider = ProviderRuntimeResolver.GetProvider();
        if (IsCodexResponsesWebSearchEnabled(provider))
        {
            return await RunCodexWebSearchAsync(input, context, cancellationToken);
        }

        // Native search handling
        return await _nativeWebSearchService.RunNativeSearchAsync(
            context,
            input.Query,
            input.AllowedDomains,
            input.BlockedDomains,
            cancellationToken);
    }

    private bool ShouldUseDuckDuckGoSearch(ToolExecutionContext context)
    {
        var provider = ProviderRuntimeResolver.GetProvider();
        if (IsCodexResponsesWebSearchEnabled(provider)) return false;

        if (provider is ApiProviderKind.Anthropic or ApiProviderKind.Vertex or ApiProviderKind.Foundry)
        {
            return false;
        }

        return !IsClaudeModel(context.AppState.MainLoopModel);
    }

    private static bool TryParseInput(string arguments, out WebSearchInput input, out string? error)
    {
        input = null!;
        error = null;
        try
        {
            var root = JsonNode.Parse(arguments)?.AsObject();
            if (root == null)
            {
                error = "Arguments must be a JSON object.";
                return false;
            }

            var query = root["query"]?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
            {
                error = "Missing 'query' parameter.";
                return false;
            }

            var allowedDomains = root["allowed_domains"]?.AsArray().Select(n => n!.ToString()).ToList();
            var blockedDomains = root["blocked_domains"]?.AsArray().Select(n => n!.ToString()).ToList();

            if (allowedDomains?.Count > 0 && blockedDomains?.Count > 0)
            {
                error = "Cannot specify both allowed_domains and blocked_domains in the same request";
                return false;
            }

            input = new WebSearchInput(query, allowedDomains, blockedDomains);
            return true;
        }
        catch (Exception ex)
        {
            error = $"Error parsing arguments: {ex.Message}";
            return false;
        }
    }

    private async Task<ToolExecutionResult> RunFirecrawlSearchAsync(WebSearchInput input, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var apiKey = Environment.GetEnvironmentVariable("FIRECRAWL_API_KEY");
        
        var query = input.Query;
        if (input.BlockedDomains?.Count > 0)
        {
            var exclusions = string.Join(" ", input.BlockedDomains.Select(d => $"-site:{d}"));
            query = $"{query} {exclusions}";
        }

        context.ReportProgress("firecrawl-search-init", new JsonObject { ["type"] = "query_update", ["query"] = query });

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.firecrawl.dev/v1/search");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            var body = new { query = query, limit = 10 };
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var response = await HttpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return Failure($"Firecrawl error: {response.StatusCode}");

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var dataArray = doc.RootElement.GetProperty("data");

            var hits = new List<WebSearchResult>();
            var results = new List<object>();
            var snippets = new StringBuilder();

            foreach (var item in dataArray.EnumerateArray())
            {
                var title = item.TryGetProperty("title", out var t) ? t.GetString() : null;
                var url = item.GetProperty("url").GetString() ?? string.Empty;
                var description = item.TryGetProperty("description", out var d) ? d.GetString() : null;

                if (input.AllowedDomains?.Count > 0)
                {
                    if (!input.AllowedDomains.Any(domain => new Uri(url).Host.EndsWith(domain))) continue;
                }

                var titleToUse = title ?? url;
                hits.Add(new WebSearchResult(titleToUse, url));
                if (!string.IsNullOrEmpty(description))
                {
                    snippets.AppendLine($"**{titleToUse}** — {description} ({url})");
                }
            }

            if (snippets.Length > 0) results.Add(snippets.ToString().Trim());
            results.Add(new WebSearchToolResult("firecrawl-search", hits));

            stopwatch.Stop();
            var structuredOutput = new JsonObject
            {
                ["query"] = input.Query,
                ["results"] = JsonSerializer.SerializeToNode(results),
                ["durationSeconds"] = stopwatch.Elapsed.TotalSeconds
            };

            context.ReportProgress("firecrawl-search", new JsonObject 
            { 
                ["type"] = "search_results_received", 
                ["resultCount"] = hits.Count, 
                ["query"] = query 
            });

            return Success(FormatOutputString(input.Query, results), structuredOutput);
        }
        catch (Exception ex)
        {
            return Failure($"Firecrawl search failed: {ex.Message}");
        }
    }

    private async Task<ToolExecutionResult> RunDuckDuckGoSearchAsync(WebSearchInput input, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        context.ReportProgress("ddg-search-init", new JsonObject { ["type"] = "query_update", ["query"] = input.Query });

        try
        {
            var url = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(input.Query)}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("ClawSharp/1.0 (WebSearchTool)");
            
            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var resultNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'result')]");
            var hits = new List<WebSearchResult>();
            var results = new List<object>();
            var snippets = new StringBuilder();

            if (resultNodes != null)
            {
                foreach (var node in resultNodes)
                {
                    var titleNode = node.SelectSingleNode(".//a[@class='result__a']");
                    var snippetNode = node.SelectSingleNode(".//a[@class='result__snippet']");
                    if (titleNode == null) continue;

                    var title = titleNode.InnerText.Trim();
                    var resultUrl = titleNode.GetAttributeValue("href", string.Empty);
                    
                    if (resultUrl.Contains("uddg="))
                    {
                        var startIndex = resultUrl.IndexOf("uddg=") + 5;
                        var endIndex = resultUrl.IndexOf('&', startIndex);
                        var uddg = (endIndex == -1) ? resultUrl.Substring(startIndex) : resultUrl.Substring(startIndex, endIndex - startIndex);
                        if (!string.IsNullOrEmpty(uddg))
                        {
                            resultUrl = Uri.UnescapeDataString(uddg);
                        }
                    }

                    try
                    {
                        var host = new Uri(resultUrl).Host;
                        if (input.BlockedDomains?.Count > 0 && input.BlockedDomains.Any(d => host.EndsWith(d))) continue;
                        if (input.AllowedDomains?.Count > 0 && !input.AllowedDomains.Any(d => host.EndsWith(d))) continue;
                    }
                    catch { continue; }

                    hits.Add(new WebSearchResult(title, resultUrl));
                    if (snippetNode != null)
                    {
                        snippets.AppendLine($"**{title}** — {snippetNode.InnerText.Trim()} ({resultUrl})");
                    }
                    if (hits.Count >= 10) break;
                }
            }

            if (snippets.Length > 0) results.Add(snippets.ToString().Trim());
            results.Add(new WebSearchToolResult("duckduckgo-search", hits));

            stopwatch.Stop();
            var structuredOutput = new JsonObject
            {
                ["query"] = input.Query,
                ["results"] = JsonSerializer.SerializeToNode(results),
                ["durationSeconds"] = stopwatch.Elapsed.TotalSeconds
            };

            context.ReportProgress("duckduckgo-search", new JsonObject 
            { 
                ["type"] = "search_results_received", 
                ["resultCount"] = hits.Count, 
                ["query"] = input.Query 
            });

            return Success(FormatOutputString(input.Query, results), structuredOutput);
        }
        catch (Exception ex)
        {
            return Failure($"DuckDuckGo search failed: {ex.Message}");
        }
    }

    private async Task<ToolExecutionResult> RunCodexWebSearchAsync(WebSearchInput input, ToolExecutionContext context, CancellationToken cancellationToken)
    {
        // Placeholder for Codex search logic as it depends on internal response structures
        return Failure("Codex web search not yet fully implemented in C#. Please use FIRECRAWL_API_KEY for robust search capabilities.");
    }

        return Success(WebSearchFormatting.FormatOutputString(input.Query, results), structuredOutput);
    }
}
