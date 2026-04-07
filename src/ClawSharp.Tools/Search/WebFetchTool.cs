using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using HtmlAgilityPack;
using ReverseMarkdown;

namespace ClawSharp.Tools;

internal sealed class WebFetchTool : BaseTool
{
    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 10
    });

    private const int MaxMarkdownLength = 100_000;

    public WebFetchTool()
        : base(
            new ToolDescriptor(
                "WebFetch",
                "Fetch and extract content from a URL",
                SearchHint: "fetch and extract content from a URL",
                InputSchema: WebFetchToolSchemas.InputSchema,
                OutputSchema: WebFetchToolSchemas.OutputSchema,
                Strict: true))
    {
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!TryParseArguments(context.Arguments, out var url, out var prompt, out var errorMessage))
        {
            return Failure(errorMessage ?? "WebFetch arguments must be a JSON object with 'url' and 'prompt'.");
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var uri = new Uri(url);
            if (uri.Scheme != "https" && uri.Scheme != "http")
            {
                return Failure("Only http and https URLs are supported.");
            }

            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/markdown"));
            request.Headers.UserAgent.ParseAdd("ClawSharp/1.0 (WebFetchTool)");

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var statusCode = (int)response.StatusCode;
            var statusText = response.ReasonPhrase ?? "Unknown";

            var contentBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "text/plain";
            var rawContent = Encoding.UTF8.GetString(contentBytes);

            string markdown;
            if (contentType.Contains("text/html"))
            {
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(rawContent);
                
                // Basic cleanup
                var nodesToIgnore = htmlDoc.DocumentNode.SelectNodes("//script|//style|//noscript|//iframe|//svg|//header|//footer|//nav");
                if (nodesToIgnore != null)
                {
                    foreach (var node in nodesToIgnore)
                    {
                        node.Remove();
                    }
                }

                var converter = new Converter();
                markdown = converter.Convert(htmlDoc.DocumentNode.OuterHtml);
            }
            else
            {
                markdown = rawContent;
            }

            if (markdown.Length > MaxMarkdownLength)
            {
                markdown = markdown[..MaxMarkdownLength] + "\n\n[Content truncated due to length...]";
            }

            // In ClawSharp, we return the markdown content directly if prompts are not yet multi-model integrated.
            // Parity note: Source calls queryHaiku here. We skip for now or provide the raw markdown.
            var result = markdown;
            if (!string.IsNullOrWhiteSpace(prompt))
            {
                result = $"[MARKDOWN CONTENT FROM {url}]\n\n{markdown}\n\n[PROMPT TO APPLY]\n{prompt}\n\n(Note: ClawSharp currently returns the raw content for processing by the main model)";
            }

            stopwatch.Stop();

            var data = new JsonObject
            {
                ["bytes"] = contentBytes.Length,
                ["code"] = statusCode,
                ["codeText"] = statusText,
                ["result"] = result,
                ["durationMs"] = stopwatch.ElapsedMilliseconds,
                ["url"] = url
            };

            return Success(result, data);
        }
        catch (Exception ex)
        {
            return Failure($"Error fetching URL: {ex.Message}");
        }
    }

    private static bool TryParseArguments(string arguments, out string url, out string prompt, out string? errorMessage)
    {
        url = string.Empty;
        prompt = string.Empty;
        errorMessage = null;

        try
        {
            var root = JsonNode.Parse(arguments)?.AsObject();
            if (root is null)
            {
                errorMessage = "Arguments must be a JSON object.";
                return false;
            }

            url = root["url"]?.ToString() ?? string.Empty;
            prompt = root["prompt"]?.ToString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(url))
            {
                errorMessage = "Missing 'url' parameter.";
                return false;
            }

            return true;
        }
        catch
        {
            errorMessage = "Invalid JSON in arguments.";
            return false;
        }
    }
}
