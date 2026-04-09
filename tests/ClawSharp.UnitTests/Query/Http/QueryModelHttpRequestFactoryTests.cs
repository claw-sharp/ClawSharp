// TS parity status: focused C# coverage for the main query-model HTTP streaming request factory; live query-model transport and streamed event parsing remain intentionally unported.
using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Query;
using ClawSharp.Tasks;
using ClawSharp.Tools;

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
    public async Task CreateStreamingRequest_Shapes_OpenAi_Compatible_Request()
    {
        var request = CreateStreamingRequest();

        var httpRequest = QueryModelHttpRequestFactory.CreateStreamingRequest(
            new QueryModelHttpClientConfig(
                "https://api.openai.test/v1",
                ApiKey: "openai-key",
                TransportKind: ModelTransportKind.OpenAiChatCompletions,
                ProviderKind: ApiProviderKind.OpenAi),
            request);

        Assert.Equal("https://api.openai.test/v1/chat/completions", httpRequest.RequestUri!.ToString());
        Assert.Equal("Bearer", httpRequest.Headers.Authorization?.Scheme);
        Assert.Equal("openai-key", httpRequest.Headers.Authorization?.Parameter);

        var body = JsonNode.Parse(await httpRequest.Content!.ReadAsStringAsync())!.AsObject();
        Assert.Equal("foundation-placeholder", body["model"]?.GetValue<string>());
        Assert.True(body["stream"]?.GetValue<bool>());
        Assert.Equal(3, body["messages"]!.AsArray().Count);
        Assert.Equal("system", body["messages"]![0]!["role"]?.GetValue<string>());
        Assert.Equal("assistant", body["messages"]![1]!["role"]?.GetValue<string>());
        Assert.Equal("tool", body["messages"]![2]!["role"]?.GetValue<string>());
        Assert.Equal(4096, body["max_completion_tokens"]?.GetValue<int>());
        Assert.Equal(true, body["stream_options"]?["include_usage"]?.GetValue<bool>());
        Assert.Single(body["tools"]!.AsArray());
    }

    [Fact]
    public async Task CreateStreamingRequest_Shapes_Codex_Request()
    {
        var request = CreateStreamingRequest();

        var httpRequest = QueryModelHttpRequestFactory.CreateStreamingRequest(
            new QueryModelHttpClientConfig(
                ProviderRuntimeResolver.DefaultCodexBaseUrl,
                ApiKey: "codex-token",
                TransportKind: ModelTransportKind.CodexResponses,
                ProviderKind: ApiProviderKind.Codex,
                AccountId: "account-1"),
            request);

        Assert.Equal($"{ProviderRuntimeResolver.DefaultCodexBaseUrl}/responses", httpRequest.RequestUri!.ToString());
        Assert.Equal("Bearer", httpRequest.Headers.Authorization?.Scheme);
        Assert.Equal("codex-token", httpRequest.Headers.Authorization?.Parameter);
        Assert.Equal("account-1", httpRequest.Headers.GetValues("chatgpt-account-id").Single());
        Assert.Equal("application/json", httpRequest.Content!.Headers.ContentType?.MediaType);
        Assert.Null(httpRequest.Content!.Headers.ContentType?.CharSet);
        Assert.Empty(httpRequest.Headers.Accept);

        var body = JsonNode.Parse(await httpRequest.Content!.ReadAsStringAsync())!.AsObject();
        Assert.Equal("foundation-placeholder", body["model"]?.GetValue<string>());
        Assert.True(body["stream"]?.GetValue<bool>());
        Assert.Equal(false, body["store"]?.GetValue<bool>());
        Assert.Equal("auto", body["tool_choice"]?.GetValue<string>());
        Assert.True(body["parallel_tool_calls"]?.GetValue<bool>());
        Assert.NotNull(body["input"]);
    }

    [Fact]
    public async Task CreateStreamingRequest_Sanitizes_OpenAi_And_Codex_Tool_Schemas()
    {
        var toolSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["path"] = new JsonObject
                {
                    ["type"] = "string",
                    ["format"] = "uri",
                    ["default"] = "https://example.com"
                },
                ["timeout"] = new JsonObject
                {
                    ["type"] = "number",
                    ["minimum"] = 0,
                    ["maximum"] = 120
                },
                ["maybe"] = new JsonObject
                {
                    ["anyOf"] = new JsonArray(
                        new JsonObject
                        {
                            ["type"] = "string",
                            ["pattern"] = "^[a-z]+$"
                        },
                        new JsonObject
                        {
                            ["type"] = "null"
                        })
                }
            },
            ["required"] = new JsonArray(JsonValue.Create("path"), JsonValue.Create("missing"))
        };
        var request = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                "session-http-2",
                "gpt-5.4",
                [new QuerySystemPromptBlock("system")],
                [new QueryRequestMessage("user", [new QueryRequestContentBlock("text", Text: "hello")])],
                [new QueryRequestTool("Fetch", "Fetches data.", toolSchema, Strict: true)],
                new QueryRequestOutputConfig(),
                [],
                MaxTokens: 4096),
            "repl_main_thread");

        var openAiRequest = QueryModelHttpRequestFactory.CreateStreamingRequest(
            new QueryModelHttpClientConfig(
                "https://api.openai.test/v1",
                ApiKey: "openai-key",
                TransportKind: ModelTransportKind.OpenAiChatCompletions,
                ProviderKind: ApiProviderKind.OpenAi),
            request);
        var codexRequest = QueryModelHttpRequestFactory.CreateStreamingRequest(
            new QueryModelHttpClientConfig(
                ProviderRuntimeResolver.DefaultCodexBaseUrl,
                ApiKey: "codex-token",
                TransportKind: ModelTransportKind.CodexResponses,
                ProviderKind: ApiProviderKind.Codex),
            request);

        var openAiBody = JsonNode.Parse(await openAiRequest.Content!.ReadAsStringAsync())!.AsObject();
        var openAiParameters = openAiBody["tools"]![0]!["function"]!["parameters"]!.AsObject();
        Assert.False(openAiParameters.ToJsonString().Contains("default", StringComparison.Ordinal));
        Assert.False(openAiParameters.ToJsonString().Contains("minimum", StringComparison.Ordinal));
        Assert.False(openAiParameters.ToJsonString().Contains("maximum", StringComparison.Ordinal));
        Assert.False(openAiParameters.ToJsonString().Contains("format", StringComparison.Ordinal));
        var openAiRequired = openAiParameters["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        Assert.Equal(["path", "timeout", "maybe"], openAiRequired);
        Assert.True(openAiParameters["properties"]!["timeout"]!["anyOf"] is JsonArray);
        Assert.True(openAiParameters["properties"]!["maybe"]!["anyOf"] is JsonArray);

        var codexBody = JsonNode.Parse(await codexRequest.Content!.ReadAsStringAsync())!.AsObject();
        var codexParameters = codexBody["tools"]![0]!["parameters"]!.AsObject();
        Assert.Equal(false, codexParameters["additionalProperties"]?.GetValue<bool>());
        Assert.Equal(3, codexParameters["required"]!.AsArray().Count);
        Assert.False(codexParameters.ToJsonString().Contains("pattern", StringComparison.Ordinal));
        Assert.False(codexParameters.ToJsonString().Contains("format", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateStreamingRequest_Shapes_SendMessage_For_Codex_Without_OneOf()
    {
        var registry = new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry());
        var sendMessage = Assert.Single(registry.All, static tool => string.Equals(tool.Name, "SendMessage", StringComparison.Ordinal));

        var request = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                "session-http-sendmessage",
                "codexplan",
                [new QuerySystemPromptBlock("system")],
                [new QueryRequestMessage("user", [new QueryRequestContentBlock("text", Text: "hello")])],
                [new QueryRequestTool(sendMessage.Name, sendMessage.Description, sendMessage.InputSchema, Strict: sendMessage.Strict)],
                new QueryRequestOutputConfig(),
                [],
                MaxTokens: 4096),
            "repl_main_thread");

        var codexRequest = QueryModelHttpRequestFactory.CreateStreamingRequest(
            new QueryModelHttpClientConfig(
                ProviderRuntimeResolver.DefaultCodexBaseUrl,
                ApiKey: "codex-token",
                TransportKind: ModelTransportKind.CodexResponses,
                ProviderKind: ApiProviderKind.Codex),
            request);

        var codexBody = JsonNode.Parse(await codexRequest.Content!.ReadAsStringAsync())!.AsObject();
        var messageSchema = codexBody["tools"]![0]!["parameters"]!["properties"]!["message"]!.AsObject();

        Assert.True(messageSchema.ContainsKey("anyOf"));
        Assert.False(messageSchema.ContainsKey("oneOf"));
        Assert.False(messageSchema.ToJsonString().Contains("\"oneOf\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateStreamingRequest_Shapes_AskUserQuestion_For_OpenAi_With_Nullable_Optional_Fields()
    {
        var registry = new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry());
        var askUserQuestion = Assert.Single(registry.All, static tool => string.Equals(tool.Name, "AskUserQuestion", StringComparison.Ordinal));

        var request = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                "session-http-askuserquestion",
                "gpt-5.4",
                [new QuerySystemPromptBlock("system")],
                [new QueryRequestMessage("user", [new QueryRequestContentBlock("text", Text: "hello")])],
                [new QueryRequestTool(askUserQuestion.Name, askUserQuestion.Description, askUserQuestion.InputSchema, Strict: askUserQuestion.Strict)],
                new QueryRequestOutputConfig(),
                [],
                MaxTokens: 4096),
            "repl_main_thread");

        var openAiRequest = QueryModelHttpRequestFactory.CreateStreamingRequest(
            new QueryModelHttpClientConfig(
                "https://api.openai.test/v1",
                ApiKey: "openai-key",
                TransportKind: ModelTransportKind.OpenAiChatCompletions,
                ProviderKind: ApiProviderKind.OpenAi),
            request);

        var openAiBody = JsonNode.Parse(await openAiRequest.Content!.ReadAsStringAsync())!.AsObject();
        var parameters = openAiBody["tools"]![0]!["function"]!["parameters"]!.AsObject();
        var required = parameters["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();

        Assert.Equal(["questions", "metadata"], required);
        Assert.False(parameters["properties"]!.AsObject().ContainsKey("answers"));
        Assert.False(parameters["properties"]!.AsObject().ContainsKey("annotations"));
        Assert.True(parameters["properties"]!["metadata"]!["anyOf"] is JsonArray);
    }

    [Fact]
    public async Task CreateStreamingRequest_Shapes_AskUserQuestion_For_Codex_Without_Runtime_Injected_Fields()
    {
        var registry = new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry());
        var askUserQuestion = Assert.Single(registry.All, static tool => string.Equals(tool.Name, "AskUserQuestion", StringComparison.Ordinal));

        var request = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                "session-http-askuserquestion-codex",
                "codexplan",
                [new QuerySystemPromptBlock("system")],
                [new QueryRequestMessage("user", [new QueryRequestContentBlock("text", Text: "hello")])],
                [new QueryRequestTool(askUserQuestion.Name, askUserQuestion.Description, askUserQuestion.InputSchema, Strict: askUserQuestion.Strict)],
                new QueryRequestOutputConfig(),
                [],
                MaxTokens: 4096),
            "repl_main_thread");

        var codexRequest = QueryModelHttpRequestFactory.CreateStreamingRequest(
            new QueryModelHttpClientConfig(
                ProviderRuntimeResolver.DefaultCodexBaseUrl,
                ApiKey: "codex-token",
                TransportKind: ModelTransportKind.CodexResponses,
                ProviderKind: ApiProviderKind.Codex),
            request);

        var codexBody = JsonNode.Parse(await codexRequest.Content!.ReadAsStringAsync())!.AsObject();
        var parameters = codexBody["tools"]![0]!["parameters"]!.AsObject();
        var required = parameters["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();

        Assert.Equal(["questions", "metadata"], required);
        Assert.False(parameters["properties"]!.AsObject().ContainsKey("answers"));
        Assert.False(parameters["properties"]!.AsObject().ContainsKey("annotations"));
        Assert.Equal(false, parameters["additionalProperties"]?.GetValue<bool>());
    }

    [Fact]
    public async Task CreateStreamingRequest_Shapes_StructuredOutput_For_OpenAi_With_Explicit_Empty_Properties()
    {
        var registry = new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry());
        var structuredOutput = Assert.Single(registry.All, static tool => string.Equals(tool.Name, "StructuredOutput", StringComparison.Ordinal));

        var request = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                "session-http-structuredoutput",
                "gpt-5.4",
                [new QuerySystemPromptBlock("system")],
                [new QueryRequestMessage("user", [new QueryRequestContentBlock("text", Text: "hello")])],
                [new QueryRequestTool(structuredOutput.Name, structuredOutput.Description, structuredOutput.InputSchema, Strict: structuredOutput.Strict)],
                new QueryRequestOutputConfig(),
                [],
                MaxTokens: 4096),
            "repl_main_thread");

        var openAiRequest = QueryModelHttpRequestFactory.CreateStreamingRequest(
            new QueryModelHttpClientConfig(
                "https://api.openai.test/v1",
                ApiKey: "openai-key",
                TransportKind: ModelTransportKind.OpenAiChatCompletions,
                ProviderKind: ApiProviderKind.OpenAi),
            request);

        var openAiBody = JsonNode.Parse(await openAiRequest.Content!.ReadAsStringAsync())!.AsObject();
        var parameters = openAiBody["tools"]![0]!["function"]!["parameters"]!.AsObject();

        Assert.Equal("object", parameters["type"]?.GetValue<string>());
        Assert.NotNull(parameters["properties"]);
        Assert.Empty(parameters["properties"]!.AsObject());
        Assert.Equal(true, parameters["additionalProperties"]?.GetValue<bool>());
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
