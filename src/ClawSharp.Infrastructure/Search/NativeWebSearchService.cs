using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Query;
using ClawSharp.Tools;

namespace ClawSharp.Infrastructure;

public sealed class NativeWebSearchService : INativeWebSearchService
{
    private readonly IQueryModelCallExecutor _modelCallExecutor;

    public NativeWebSearchService(IQueryModelCallExecutor modelCallExecutor)
    {
        _modelCallExecutor = modelCallExecutor;
    }

    public async Task<ToolExecutionResult> RunNativeSearchAsync(
        ToolExecutionContext context,
        string query,
        List<string>? allowedDomains,
        List<string>? blockedDomains,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTimeOffset.UtcNow;
        var sessionId = context.Session.Id;
        
        var model = context.AppState.MainLoopModel?.Contains("claude", StringComparison.OrdinalIgnoreCase) == true 
            ? "claude-3-5-haiku-20241022" 
            : context.AppState.MainLoopModel ?? "claude-3-5-haiku-20241022";

        var toolSchema = new QueryRequestTool(
            Name: "web_search",
            Description: "Search the web for current information",
            Type: "web_search_20250305",
            InputSchema: new JsonObject { ["query"] = query }
        );

        var userMessage = ChatMessageFactory.CreateText(MessageRole.User, $"Perform a web search for the query: {query}");
        
        var request = new QueryModelRequest(
            sessionId,
            model,
            [new QuerySystemPromptBlock("You are an assistant for performing a web search tool use. Return findings clearly.")],
            [UserMessageToRequest(userMessage)],
            [toolSchema],
            new QueryRequestOutputConfig(),
            Betas: ["web-search-2025-03-05"],
            MaxTokens: 4096
        );

        var streamingRequest = new QueryModelHttpStreamingRequest(request, "web_search_tool");
        var loopState = new QueryLoopState([userMessage], 0, new QueryToolUseContextState(context.ReadFileState, context.ToolPermissionContext, model));

        var results = new List<object>();

        try 
        {
            await foreach (var update in _modelCallExecutor.StreamAsync(
                streamingRequest,
                QueryTurnRequest.Create(context.Session, query),
                loopState,
                context.Session,
                context.Settings,
                cancellationToken))
            {
                if (update.RuntimeEvent is QueryMessageRuntimeEvent messageEvent)
                {
                    foreach (var block in messageEvent.Message.ContentBlocks)
                    {
                        if (block.Kind == MessageContentKind.WebSearchToolResult && !string.IsNullOrEmpty(block.Value))
                        {
                            try 
                            {
                                using var doc = JsonDocument.Parse(block.Value);
                                var hits = new List<WebSearchResult>();
                                foreach (var hit in doc.RootElement.EnumerateArray())
                                {
                                    var title = hit.TryGetProperty("title", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                                    var url = hit.TryGetProperty("url", out var u) ? u.GetString() ?? string.Empty : string.Empty;
                                    hits.Add(new WebSearchResult(title, url));
                                }
                                
                                results.Add(new WebSearchToolResult(block.Metadata?.GetValueOrDefault("toolUseId") ?? "native-search", hits));
                            }
                            catch { /* ignore malformed server blocks */ }
                        }
                        else if (block.Kind == MessageContentKind.Text && !string.IsNullOrEmpty(block.Value))
                        {
                            results.Add(block.Value);
                        }
                    }
                }
            }

            var duration = (DateTimeOffset.UtcNow - startTime).TotalSeconds;
            var structuredOutput = new JsonObject
            {
                ["query"] = query,
                ["results"] = JsonSerializer.SerializeToNode(results),
                ["durationSeconds"] = duration
            };

            return new ToolExecutionResult(true, WebSearchFormatting.FormatOutputString(query, results), structuredOutput);
        }
        catch (Exception ex)
        {
            return new ToolExecutionResult(false, $"Native web search failed: {ex.Message}");
        }
    }

    private static QueryRequestMessage UserMessageToRequest(ChatMessage message)
    {
        return new QueryRequestMessage(
            "user",
            [new QueryRequestContentBlock("text", Text: message.ContentBlocks[0].Value)]
        );
    }
}
