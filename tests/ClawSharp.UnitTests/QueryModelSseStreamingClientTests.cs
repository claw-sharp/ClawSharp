// TS origin: ./services/api/claude.ts
// TS parity status: focused coverage for the raw query-model SSE transport layer; higher-level Anthropic event parsing remains intentionally delegated to the separate stream-update parser contract.
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public sealed class QueryModelSseStreamingClientTests
{
    [Fact]
    public async Task StreamAsync_Sends_Ts_Shaped_Request_And_Yields_Data_Payloads()
    {
        HttpRequestMessage? capturedRequest = null;
        var body = string.Join(
            "\n",
            [
                "event: message_start",
                "data: {\"type\":\"message_start\"}",
                string.Empty,
                ": ping",
                "id: 2",
                "data: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"hi\"}}",
                string.Empty,
                "data: [DONE]",
                string.Empty
            ]);
        using var httpClient = new HttpClient(new StubHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
            };
        }));
        var client = new QueryModelSseStreamingClient(httpClient);

        var payloads = new List<JsonNode>();
        await foreach (var payload in client.StreamAsync(
                           new QueryModelHttpClientConfig("https://api.anthropic.test", "test-key"),
                           CreateStreamingRequest()))
        {
            payloads.Add(payload);
        }

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("https://api.anthropic.test/v1/messages", capturedRequest.RequestUri!.ToString());
        Assert.Equal(QueryModelHttpRequestFactory.AnthropicVersion, capturedRequest.Headers.GetValues("anthropic-version").Single());
        Assert.Equal("test-key", capturedRequest.Headers.GetValues("x-api-key").Single());
        Assert.Equal(2, payloads.Count);
        Assert.Equal("message_start", payloads[0]["type"]?.GetValue<string>());
        Assert.Equal("content_block_delta", payloads[1]["type"]?.GetValue<string>());
    }

    [Fact]
    public async Task StreamAsync_Flushes_Trailing_Data_Frame_Without_Terminating_Blank_Line()
    {
        using var httpClient = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("data: {\"type\":\"message_stop\"}", Encoding.UTF8, "text/event-stream")
            }));
        var client = new QueryModelSseStreamingClient(httpClient);

        var payloads = new List<JsonNode>();
        await foreach (var payload in client.StreamAsync(
                           new QueryModelHttpClientConfig("https://api.anthropic.test"),
                           CreateStreamingRequest()))
        {
            payloads.Add(payload);
        }

        Assert.Single(payloads);
        Assert.Equal("message_stop", payloads[0]["type"]?.GetValue<string>());
    }

    [Fact]
    public async Task StreamAsync_Throws_On_Non_Success_Status()
    {
        using var httpClient = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("""{"error":"bad_gateway"}""", Encoding.UTF8, "application/json")
            }));
        var client = new QueryModelSseStreamingClient(httpClient);

        var exception = await Assert.ThrowsAsync<QueryModelApiException>(async () =>
        {
            await foreach (var _ in client.StreamAsync(
                               new QueryModelHttpClientConfig("https://api.anthropic.test"),
                               CreateStreamingRequest()))
            {
            }
        });

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Equal("""{"error":"bad_gateway"}""", exception.ResponseBody);
    }

    [Fact]
    public async Task StreamAsync_Throws_Overloaded_Exception_On_529_Response()
    {
        using var httpClient = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage((HttpStatusCode)529)
            {
                Content = new StringContent(
                    """{"type":"error","error":{"type":"overloaded_error","message":"busy"}}""",
                    Encoding.UTF8,
                    "application/json")
            }));
        var client = new QueryModelSseStreamingClient(httpClient);

        var exception = await Assert.ThrowsAsync<QueryModelOverloadedException>(async () =>
        {
            await foreach (var _ in client.StreamAsync(
                               new QueryModelHttpClientConfig("https://api.anthropic.test"),
                               CreateStreamingRequest()))
            {
            }
        });

        Assert.Equal((HttpStatusCode)529, exception.StatusCode);
        Assert.Contains("\"type\":\"overloaded_error\"", exception.ResponseBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamAsync_Normalizes_OpenAi_Stream_To_Anthropic_Shaped_Events()
    {
        using var httpClient = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    string.Join(
                        "\n",
                        [
                            "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}",
                            string.Empty,
                            "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":11,\"completion_tokens\":7}}",
                            string.Empty,
                            "data: [DONE]",
                            string.Empty
                        ]),
                    Encoding.UTF8,
                    "text/event-stream")
            }));
        var client = new QueryModelSseStreamingClient(httpClient);

        var payloads = new List<JsonNode>();
        await foreach (var payload in client.StreamAsync(
                           new QueryModelHttpClientConfig(
                               "https://api.openai.test/v1",
                               ApiKey: "openai-key",
                               TransportKind: ModelTransportKind.OpenAiChatCompletions,
                               ProviderKind: ApiProviderKind.OpenAi),
                           CreateStreamingRequest()))
        {
            payloads.Add(payload);
        }

        Assert.Equal("message_start", payloads[0]["type"]?.GetValue<string>());
        Assert.Contains(payloads, payload => payload["type"]?.GetValue<string>() == "content_block_start");
        Assert.Contains(payloads, payload => payload["type"]?.GetValue<string>() == "content_block_delta");
        Assert.Contains(payloads, payload => payload["type"]?.GetValue<string>() == "message_delta");
        Assert.Equal("message_stop", payloads[^1]["type"]?.GetValue<string>());
    }

    [Fact]
    public async Task StreamAsync_Normalizes_Codex_Stream_To_Anthropic_Shaped_Events()
    {
        using var httpClient = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    string.Join(
                        "\n",
                        [
                            "event: response.output_text.delta",
                            "data: {\"delta\":\"Hello\"}",
                            string.Empty,
                            "event: response.completed",
                            "data: {\"response\":{\"usage\":{\"input_tokens\":4,\"output_tokens\":5},\"output\":[{\"type\":\"message\"}]}}",
                            string.Empty
                        ]),
                    Encoding.UTF8,
                    "text/event-stream")
            }));
        var client = new QueryModelSseStreamingClient(httpClient);

        var payloads = new List<JsonNode>();
        await foreach (var payload in client.StreamAsync(
                           new QueryModelHttpClientConfig(
                               ProviderRuntimeResolver.DefaultCodexBaseUrl,
                               ApiKey: "codex-token",
                               TransportKind: ModelTransportKind.CodexResponses,
                               ProviderKind: ApiProviderKind.Codex,
                               AccountId: "account-1"),
                           CreateStreamingRequest()))
        {
            payloads.Add(payload);
        }

        Assert.Equal("message_start", payloads[0]["type"]?.GetValue<string>());
        Assert.Contains(payloads, payload => payload["type"]?.GetValue<string>() == "content_block_delta");
        Assert.Contains(payloads, payload => payload["type"]?.GetValue<string>() == "message_delta");
        Assert.Equal("message_stop", payloads[^1]["type"]?.GetValue<string>());
    }

    private static QueryModelHttpStreamingRequest CreateStreamingRequest()
    {
        return new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                "session-sse",
                "foundation-placeholder",
                [new QuerySystemPromptBlock("system")],
                [new QueryRequestMessage("user", [new QueryRequestContentBlock("text", Text: "hello")])],
                [],
                new QueryRequestOutputConfig(),
                []),
            "repl_main_thread");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }
}
