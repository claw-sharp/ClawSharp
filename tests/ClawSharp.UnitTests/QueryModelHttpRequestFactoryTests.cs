// TS origin: ./services/api/claude.ts
// TS parity status: focused C# coverage for the main query-model HTTP streaming request factory; live query-model transport and streamed event parsing remain intentionally unported.
using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public sealed class QueryModelHttpRequestFactoryTests
{
    [Fact]
    public async Task CreateStreamingRequest_Shapes_Anthropic_Headers_And_Stream_Body()
    {
        var request = CreateStreamingRequest();

        var httpRequest = QueryModelHttpRequestFactory.CreateStreamingRequest(
            new QueryModelHttpClientConfig("https://api.anthropic.test", "test-key"),
            request);

        Assert.Equal(HttpMethod.Post, httpRequest.Method);
        Assert.Equal("https://api.anthropic.test/v1/messages", httpRequest.RequestUri!.ToString());
        Assert.Equal(QueryModelHttpRequestFactory.AnthropicVersion, httpRequest.Headers.GetValues("anthropic-version").Single());
        Assert.Equal("test-key", httpRequest.Headers.GetValues("x-api-key").Single());
        Assert.Equal("beta-one,beta-two", httpRequest.Headers.GetValues("anthropic-beta").Single());

        var body = JsonNode.Parse(await httpRequest.Content!.ReadAsStringAsync())!.AsObject();
        Assert.Equal("foundation-placeholder", body["model"]?.GetValue<string>());
        Assert.True(body["stream"]?.GetValue<bool>());
        Assert.Equal(2, body["messages"]!.AsArray().Count);
        Assert.Equal(2, body["system"]!.AsArray().Count);
        Assert.Equal("text", body["system"]![0]!["type"]?.GetValue<string>());
        Assert.Equal("tokens", body["output_config"]?["task_budget"]?["type"]?.GetValue<string>());
        Assert.Single(body["tools"]!.AsArray());
        Assert.Equal(4096, body["max_tokens"]?.GetValue<int>());
        Assert.Equal("enabled", body["thinking"]?["type"]?.GetValue<string>());
        Assert.Equal(2048, body["thinking"]?["budget_tokens"]?.GetValue<int>());
        Assert.Equal(1200, body["output_config"]?["task_budget"]?["total"]?.GetValue<int>());
        Assert.Equal(700, body["output_config"]?["task_budget"]?["remaining"]?.GetValue<int>());
    }

    [Fact]
    public void CreateRequestBody_Includes_MaxTokens_For_Default_Builder_Request()
    {
        var builder = new QueryRequestBuilder();
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-http-request-factory-default-max-tokens", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var session = new DefaultSessionFactory(tempDir).Create();
        var turnRequest = QueryTurnRequest.Create(session, "hello");
        var modelRequest = builder.Build(
            turnRequest,
            session,
            new ClawSharpSettings(),
            []);

        var body = QueryModelHttpRequestFactory.CreateRequestBody(
            new QueryModelHttpStreamingRequest(modelRequest, "repl_main_thread"));

        Assert.Equal(32_000, body["max_tokens"]?.GetValue<int>());
    }

    [Fact]
    public void CreateStreamingRequest_Uses_Bearer_Authorization_When_AuthToken_Is_Configured()
    {
        var request = CreateStreamingRequest();

        var httpRequest = QueryModelHttpRequestFactory.CreateStreamingRequest(
            new QueryModelHttpClientConfig("https://api.anthropic.test", AuthToken: "oauth-token"),
            request);

        Assert.Equal("Bearer", httpRequest.Headers.Authorization?.Scheme);
        Assert.Equal("oauth-token", httpRequest.Headers.Authorization?.Parameter);
        Assert.False(httpRequest.Headers.Contains("x-api-key"));
    }

    [Fact]
    public void CreateRequestBody_Parses_Tool_Use_Input_And_Tool_Result_Structured_Output()
    {
        var body = QueryModelHttpRequestFactory.CreateRequestBody(CreateStreamingRequest());
        var messages = body["messages"]!.AsArray();
        var assistantToolUse = messages[0]!["content"]!.AsArray()[1]!.AsObject();
        var userToolResult = messages[1]!["content"]!.AsArray()[0]!.AsObject();

        Assert.Equal("tool_use", assistantToolUse["type"]?.GetValue<string>());
        Assert.Equal("Read", assistantToolUse["name"]?.GetValue<string>());
        Assert.Equal("tooluse-read", assistantToolUse["tool_use_id"]?.GetValue<string>());
        Assert.Equal("note.txt", assistantToolUse["input"]?["file_path"]?.GetValue<string>());

        Assert.Equal("tool_result", userToolResult["type"]?.GetValue<string>());
        Assert.Equal("tooluse-read", userToolResult["tool_use_id"]?.GetValue<string>());
        Assert.Equal("success", userToolResult["structured_output"]?["status"]?.GetValue<string>());
    }

    private static QueryModelHttpStreamingRequest CreateStreamingRequest()
    {
        return new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                "session-http-1",
                "foundation-placeholder",
                [
                    new QuerySystemPromptBlock("system static"),
                    new QuerySystemPromptBlock("system dynamic", CacheControl: new QueryRequestCacheControl("ephemeral", "global"))
                ],
                [
                    new QueryRequestMessage(
                        "assistant",
                        [
                            new QueryRequestContentBlock("text", Text: "Read note.txt"),
                            new QueryRequestContentBlock("tool_use", Name: "Read", ToolUseId: "tooluse-read", Input: """{"file_path":"note.txt"}""")
                        ]),
                    new QueryRequestMessage(
                        "user",
                        [
                            new QueryRequestContentBlock("tool_result", Text: "hello", ToolUseId: "tooluse-read", StructuredOutput: """{"status":"success"}""")
                        ])
                ],
                [
                    new QueryRequestTool(
                        "Read",
                        "Reads a file.",
                        new JsonObject
                        {
                            ["type"] = "object"
                        },
                        Strict: true)
                ],
                new QueryRequestOutputConfig(new QueryTaskBudget(1200, 700)),
                ["beta-one", "beta-two"],
                MaxTokens: 4096,
                Thinking: new QueryThinkingConfig("enabled", 2048)),
            "repl_main_thread");
    }
}
