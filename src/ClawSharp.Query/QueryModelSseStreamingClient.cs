// TS origin: ./services/api/claude.ts
// TS parity status: ports the raw SSE transport layer beneath the main query-model HTTP streaming client by extracting streamed `data:` JSON payloads from the Anthropic-style `/v1/messages` response; concrete event parsing still remains delegated to the higher-level stream-update parser.
using System.Text;
using System.Text.Json.Nodes;
using System.Net.Http.Headers;

namespace ClawSharp.Query;

public sealed class QueryModelSseStreamingClient : IQueryModelHttpStreamingClient
{
    private readonly HttpClient _httpClient;

    public QueryModelSseStreamingClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async IAsyncEnumerable<JsonNode> StreamAsync(
        QueryModelHttpClientConfig config,
        QueryModelHttpStreamingRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var httpRequest = QueryModelHttpRequestFactory.CreateStreamingRequest(config, request);
        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = response.Content is null
                ? null
                : await response.Content.ReadAsStringAsync(cancellationToken);
            var responseHeaders = CreateHeaders(response);
            if (QueryModelOverloadedException.IsOverloaded(response.StatusCode, responseBody))
            {
                throw new QueryModelOverloadedException(response.StatusCode, responseHeaders, responseBody);
            }

            throw new QueryModelApiException(response.StatusCode, responseHeaders, responseBody);
        }

        var responseContent = response.Content ?? throw new InvalidOperationException("Streamed model response content was null.");
        await using var responseStream = await responseContent.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(responseStream);
        var dataBuilder = new StringBuilder();

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            if (line.Length == 0)
            {
                if (TryParsePayload(dataBuilder, out var payload))
                {
                    yield return payload;
                }

                continue;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line["data:".Length..];
            if (data.StartsWith(' '))
            {
                data = data[1..];
            }

            dataBuilder.AppendLine(data);
        }

        if (TryParsePayload(dataBuilder, out var trailingPayload))
        {
            yield return trailingPayload;
        }
    }

    private static bool TryParsePayload(StringBuilder dataBuilder, out JsonNode payload)
    {
        payload = null!;

        if (dataBuilder.Length == 0)
        {
            return false;
        }

        var rawPayload = dataBuilder.ToString().TrimEnd('\r', '\n');
        dataBuilder.Clear();

        if (string.IsNullOrWhiteSpace(rawPayload) ||
            string.Equals(rawPayload, "[DONE]", StringComparison.Ordinal))
        {
            return false;
        }

        payload = JsonNode.Parse(rawPayload) ?? throw new InvalidOperationException("Streamed model payload parsed to null.");
        return true;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> CreateHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        AddHeaders(headers, response.Headers);
        if (response.Content is not null)
        {
            AddHeaders(headers, response.Content.Headers);
        }

        return headers;
    }

    private static void AddHeaders(
        IDictionary<string, IReadOnlyList<string>> target,
        HttpHeaders headers)
    {
        foreach (var header in headers)
        {
            target[header.Key] = header.Value.ToArray();
        }
    }
}
