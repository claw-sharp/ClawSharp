using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Tools;

public interface INativeWebSearchService
{
    Task<ToolExecutionResult> RunNativeSearchAsync(
        ToolExecutionContext context,
        string query,
        List<string>? allowedDomains,
        List<string>? blockedDomains,
        CancellationToken cancellationToken = default);
}

public sealed class NullNativeWebSearchService : INativeWebSearchService
{
    public Task<ToolExecutionResult> RunNativeSearchAsync(
        ToolExecutionContext context,
        string query,
        List<string>? allowedDomains,
        List<string>? blockedDomains,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ToolExecutionResult(false, "Native web search is not yet implemented in ClawSharp. Please configure FIRECRAWL_API_KEY for robust search capabilities."));
    }
}
