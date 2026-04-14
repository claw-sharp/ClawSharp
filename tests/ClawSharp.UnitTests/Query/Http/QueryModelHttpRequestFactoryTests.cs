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
    public async Task CreateStreamingRequest_Shapes_Anthropic_User_Image_Content_As_Native_Image_Block()
    {
        var request = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                "session-http-anthropic-image",
                "claude-haiku-4-5-20251001",
                [new QuerySystemPromptBlock("system")],
                [
                    new QueryRequestMessage(
                        "user",
                        [
                            new QueryRequestContentBlock("text", Text: "Describe this picture"),
                            new QueryRequestContentBlock(
                                "image",
                                ImageSource: new QueryRequestImageSource("base64", "image/png", "YWJjMTIz"))
                        ])
                ],
                [],
                new QueryRequestOutputConfig(),
                [],
                MaxTokens: 4096),
            "repl_main_thread");

        var httpRequest = QueryModelHttpRequestFactory.CreateStreamingRequest(
            new QueryModelHttpClientConfig("https://api.anthropic.test", ApiKey: "test-key"),
            request);

        var body = JsonNode.Parse(await httpRequest.Content!.ReadAsStringAsync())!.AsObject();
        var userMessage = body["messages"]![0]!.AsObject();
        var content = userMessage["content"]!.AsArray();

        Assert.Equal("user", userMessage["role"]?.GetValue<string>());
        Assert.Equal("text", content[0]!["type"]?.GetValue<string>());
        Assert.Equal("Describe this picture", content[0]!["text"]?.GetValue<string>());
        Assert.Equal("image", content[1]!["type"]?.GetValue<string>());
        Assert.Equal("base64", content[1]!["source"]!["type"]?.GetValue<string>());
        Assert.Equal("image/png", content[1]!["source"]!["media_type"]?.GetValue<string>());
        Assert.Equal("YWJjMTIz", content[1]!["source"]!["data"]?.GetValue<string>());
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
    public async Task CreateStreamingRequest_Shapes_OpenAi_User_Image_Content_As_ImageUrl_Parts()
    {
        var request = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                "session-http-openai-image",
                "gpt-4o",
                [new QuerySystemPromptBlock("system")],
                [
                    new QueryRequestMessage(
                        "user",
                        [
                            new QueryRequestContentBlock("text", Text: "Describe this picture"),
                            new QueryRequestContentBlock(
                                "image",
                                ImageSource: new QueryRequestImageSource("base64", "image/png", "YWJjMTIz"))
                        ])
                ],
                [],
                new QueryRequestOutputConfig(),
                [],
                MaxTokens: 4096),
            "repl_main_thread");

        var httpRequest = QueryModelHttpRequestFactory.CreateStreamingRequest(
            new QueryModelHttpClientConfig(
                "https://api.openai.test/v1",
                ApiKey: "openai-key",
                TransportKind: ModelTransportKind.OpenAiChatCompletions,
                ProviderKind: ApiProviderKind.OpenAi),
            request);

        var body = JsonNode.Parse(await httpRequest.Content!.ReadAsStringAsync())!.AsObject();
        var userMessage = body["messages"]![1]!.AsObject();
        var content = userMessage["content"]!.AsArray();

        Assert.Equal("user", userMessage["role"]?.GetValue<string>());
        Assert.Equal("text", content[0]!["type"]?.GetValue<string>());
        Assert.Equal("Describe this picture", content[0]!["text"]?.GetValue<string>());
        Assert.Equal("image_url", content[1]!["type"]?.GetValue<string>());
        Assert.Equal(
            "data:image/png;base64,YWJjMTIz",
            content[1]!["image_url"]!["url"]?.GetValue<string>());
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
    public async Task CreateStreamingRequest_Shapes_Codex_Request_With_Previous_Response_Items()
    {
        var request = new QueryModelHttpStreamingRequest(
            CreateStreamingRequest().Request with
            {
                PreviousResponseItems =
                [
                    """{"type":"reasoning","id":"rs_123","summary":[]}""",
                    """{"type":"function_call","id":"fc_123","call_id":"call_123","name":"mcp__linear__list_teams","arguments":"{}"}"""
                ]
            },
            "repl_main_thread");

        var httpRequest = QueryModelHttpRequestFactory.CreateStreamingRequest(
            new QueryModelHttpClientConfig(
                ProviderRuntimeResolver.DefaultCodexBaseUrl,
                ApiKey: "codex-token",
                TransportKind: ModelTransportKind.CodexResponses,
                ProviderKind: ApiProviderKind.Codex,
                AccountId: "account-1"),
            request);

        var body = JsonNode.Parse(await httpRequest.Content!.ReadAsStringAsync())!.AsObject();
        Assert.Null(body["previous_response_id"]);
        var input = body["input"]!.AsArray();
        Assert.Equal("reasoning", input[0]!["type"]?.GetValue<string>());
        Assert.Equal("function_call", input[1]!["type"]?.GetValue<string>());
    }

    [Fact]
    public async Task CreateStreamingRequest_Replays_Codex_Function_Call_With_Preserved_Item_Id()
    {
        var request = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                "session-http-codex-tool-replay",
                "gpt-5.4",
                [new QuerySystemPromptBlock("system")],
                [
                    new QueryRequestMessage(
                        "user",
                        [new QueryRequestContentBlock("text", Text: "Create a project")]),
                    new QueryRequestMessage(
                        "assistant",
                        [
                            new QueryRequestContentBlock(
                                "tool_use",
                                Name: "mcp__linear__save_project",
                                ToolUseId: "call_4TOTDvJLfYXDCf5jJASwe0Cv",
                                ToolCallItemId: "fc_preserved_item_123",
                                Input: """{"name":"HR"}""")
                        ]),
                    new QueryRequestMessage(
                        "user",
                        [
                            new QueryRequestContentBlock(
                                "tool_result",
                                Text: "Error: setTeams must contain at least one team",
                                ToolUseId: "call_4TOTDvJLfYXDCf5jJASwe0Cv")
                        ])
                ],
                [
                    new QueryRequestTool(
                        "mcp__linear__save_project",
                        "Creates or updates a Linear project.",
                        new JsonObject { ["type"] = "object" },
                        Strict: true)
                ],
                new QueryRequestOutputConfig(),
                [],
                MaxTokens: 4096),
            "repl_main_thread");

        var httpRequest = QueryModelHttpRequestFactory.CreateStreamingRequest(
            new QueryModelHttpClientConfig(
                ProviderRuntimeResolver.DefaultCodexBaseUrl,
                ApiKey: "codex-token",
                TransportKind: ModelTransportKind.CodexResponses,
                ProviderKind: ApiProviderKind.Codex),
            request);

        var body = JsonNode.Parse(await httpRequest.Content!.ReadAsStringAsync())!.AsObject();
        var input = body["input"]!.AsArray();

        Assert.Equal("message", input[0]!["type"]?.GetValue<string>());
        Assert.Equal("function_call", input[1]!["type"]?.GetValue<string>());
        Assert.Equal("fc_preserved_item_123", input[1]!["id"]?.GetValue<string>());
        Assert.Equal("call_4TOTDvJLfYXDCf5jJASwe0Cv", input[1]!["call_id"]?.GetValue<string>());
        Assert.Equal("function_call_output", input[2]!["type"]?.GetValue<string>());
        Assert.Equal("call_4TOTDvJLfYXDCf5jJASwe0Cv", input[2]!["call_id"]?.GetValue<string>());
    }

    [Fact]
    public async Task CreateStreamingRequest_Replays_Codex_Function_Call_Output_When_Previous_Response_Items_Are_Present()
    {
        var request = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                "session-http-codex-post-tool",
                "gpt-5.4",
                [new QuerySystemPromptBlock("system")],
                [
                    new QueryRequestMessage(
                        "user",
                        [
                            new QueryRequestContentBlock(
                                "tool_result",
                                Text: "Found team TruckerPoints",
                                ToolUseId: "call_ADNkZPvZgiaz4XFMAei8STuF")
                        ])
                ],
                [
                    new QueryRequestTool(
                        "mcp__linear__list_teams",
                        "Lists teams in Linear.",
                        new JsonObject { ["type"] = "object" },
                        Strict: true)
                ],
                new QueryRequestOutputConfig(),
                [],
                MaxTokens: 4096,
                PreviousResponseItems:
                [
                    """{"type":"reasoning","id":"rs_123","summary":[]}""",
                    """{"type":"function_call","id":"fc_08f6807deaae1fa00169de447f39208191b81f914bec7d3167","call_id":"call_ADNkZPvZgiaz4XFMAei8STuF","name":"mcp__linear__list_teams","arguments":"{}"}"""
                ]),
            "repl_main_thread");

        var httpRequest = QueryModelHttpRequestFactory.CreateStreamingRequest(
            new QueryModelHttpClientConfig(
                ProviderRuntimeResolver.DefaultCodexBaseUrl,
                ApiKey: "codex-token",
                TransportKind: ModelTransportKind.CodexResponses,
                ProviderKind: ApiProviderKind.Codex),
            request);

        var body = JsonNode.Parse(await httpRequest.Content!.ReadAsStringAsync())!.AsObject();
        var input = body["input"]!.AsArray();

        Assert.Equal("reasoning", input[0]!["type"]?.GetValue<string>());
        Assert.Equal("function_call", input[1]!["type"]?.GetValue<string>());
        Assert.Equal("function_call_output", input[2]!["type"]?.GetValue<string>());
        Assert.Equal("call_ADNkZPvZgiaz4XFMAei8STuF", input[2]!["call_id"]?.GetValue<string>());
        Assert.Equal("Found team TruckerPoints", input[2]!["output"]?.GetValue<string>());
    }

    [Fact]
    public async Task CreateStreamingRequest_Shapes_Codex_User_Image_Content_As_InputImage_Parts()
    {
        var request = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                "session-http-codex-image",
                "gpt-5.4",
                [new QuerySystemPromptBlock("system")],
                [
                    new QueryRequestMessage(
                        "user",
                        [
                            new QueryRequestContentBlock("text", Text: "Describe this picture"),
                            new QueryRequestContentBlock(
                                "image",
                                ImageSource: new QueryRequestImageSource("base64", "image/png", "YWJjMTIz"))
                        ])
                ],
                [],
                new QueryRequestOutputConfig(),
                [],
                MaxTokens: 4096),
            "repl_main_thread");

        var httpRequest = QueryModelHttpRequestFactory.CreateStreamingRequest(
            new QueryModelHttpClientConfig(
                ProviderRuntimeResolver.DefaultCodexBaseUrl,
                ApiKey: "codex-token",
                TransportKind: ModelTransportKind.CodexResponses,
                ProviderKind: ApiProviderKind.Codex),
            request);

        var body = JsonNode.Parse(await httpRequest.Content!.ReadAsStringAsync())!.AsObject();
        var inputMessage = body["input"]![0]!.AsObject();
        var content = inputMessage["content"]!.AsArray();

        Assert.Equal("message", inputMessage["type"]?.GetValue<string>());
        Assert.Equal("user", inputMessage["role"]?.GetValue<string>());
        Assert.Equal("input_text", content[0]!["type"]?.GetValue<string>());
        Assert.Equal("Describe this picture", content[0]!["text"]?.GetValue<string>());
        Assert.Equal("input_image", content[1]!["type"]?.GetValue<string>());
        Assert.Equal("data:image/png;base64,YWJjMTIz", content[1]!["image_url"]?.GetValue<string>());
    }

    [Fact]
    public async Task CreateStreamingRequest_Strips_MaxItems_From_Anthropic_Tool_Schemas()
    {
        var toolSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["options"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "string"
                    },
                    ["minItems"] = 4,
                    ["maxItems"] = 4
                },
                ["nested"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["choices"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["items"] = new JsonObject
                            {
                                ["type"] = "string"
                            },
                            ["minItems"] = 2
                        }
                    }
                },
                ["timeout"] = new JsonObject
                {
                    ["type"] = "number",
                    ["minimum"] = 0,
                    ["maximum"] = 300000
                }
            }
        };
        var request = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                "session-http-anthropic-schema",
                "claude-haiku-4-5-20251001",
                [new QuerySystemPromptBlock("system")],
                [new QueryRequestMessage("user", [new QueryRequestContentBlock("text", Text: "hello")])],
                [new QueryRequestTool("AskUserQuestion", "Asks follow-up questions.", toolSchema, Strict: true, Type: "custom")],
                new QueryRequestOutputConfig(),
                [],
                MaxTokens: 4096),
            "repl_main_thread");

        var httpRequest = QueryModelHttpRequestFactory.CreateStreamingRequest(
            new QueryModelHttpClientConfig("https://api.anthropic.test", ApiKey: "anthropic-key"),
            request);

        var body = JsonNode.Parse(await httpRequest.Content!.ReadAsStringAsync())!.AsObject();
        var inputSchema = body["tools"]![0]!["input_schema"]!.AsObject();
        var optionsSchema = inputSchema["properties"]!["options"]!.AsObject();
        var nestedChoicesSchema = inputSchema["properties"]!["nested"]!["properties"]!["choices"]!.AsObject();

        Assert.False(inputSchema.ToJsonString().Contains("maxItems", StringComparison.Ordinal));
        Assert.False(inputSchema.ToJsonString().Contains("minimum", StringComparison.Ordinal));
        Assert.False(inputSchema.ToJsonString().Contains("maximum", StringComparison.Ordinal));
        Assert.Equal(1, optionsSchema["minItems"]?.GetValue<int>());
        Assert.Equal(1, nestedChoicesSchema["minItems"]?.GetValue<int>());
        Assert.Null(inputSchema["required"]);
        Assert.Equal("custom", body["tools"]![0]!["type"]?.GetValue<string>());
    }

    [Fact]
    public async Task CreateStreamingRequest_Preserves_MinLength_For_Anthropic_String_Tool_Fields()
    {
        var toolSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["pattern"] = new JsonObject
                {
                    ["type"] = "string",
                    ["minLength"] = 1
                }
            },
            ["required"] = new JsonArray("pattern")
        };

        var request = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                "session-http-anthropic-min-length",
                "claude-haiku-4-5-20251001",
                [new QuerySystemPromptBlock("system")],
                [new QueryRequestMessage("user", [new QueryRequestContentBlock("text", Text: "hello")])],
                [new QueryRequestTool("Glob", "Find files.", toolSchema, Strict: true, Type: "custom")],
                new QueryRequestOutputConfig(),
                [],
                MaxTokens: 4096),
            "repl_main_thread");

        var httpRequest = QueryModelHttpRequestFactory.CreateStreamingRequest(
            new QueryModelHttpClientConfig("https://api.anthropic.test", ApiKey: "anthropic-key"),
            request);

        var body = JsonNode.Parse(await httpRequest.Content!.ReadAsStringAsync())!.AsObject();
        Assert.Equal(1, body["tools"]![0]!["input_schema"]!["properties"]!["pattern"]!["minLength"]?.GetValue<int>());
    }

    [Fact]
    public async Task CreateStreamingRequest_Does_Not_Emit_Anthropic_Strict_Tools()
    {
        var tools = Enumerable.Range(1, 25)
            .Select(index => new QueryRequestTool(
                $"Tool{index}",
                $"Tool {index}",
                new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["value"] = new JsonObject
                        {
                            ["type"] = "string"
                        }
                    }
                },
                Strict: true,
                Type: "custom"))
            .ToArray();

        var request = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                "session-http-anthropic-strict-limit",
                "claude-haiku-4-5-20251001",
                [new QuerySystemPromptBlock("system")],
                [new QueryRequestMessage("user", [new QueryRequestContentBlock("text", Text: "hello")])],
                tools,
                new QueryRequestOutputConfig(),
                [],
                MaxTokens: 4096),
            "repl_main_thread");

        var httpRequest = QueryModelHttpRequestFactory.CreateStreamingRequest(
            new QueryModelHttpClientConfig("https://api.anthropic.test", ApiKey: "anthropic-key"),
            request);

        var body = JsonNode.Parse(await httpRequest.Content!.ReadAsStringAsync())!.AsObject();
        var requestTools = body["tools"]!.AsArray();
        var strictCount = requestTools.Count(tool => tool?["strict"]?.GetValue<bool>() == true);

        Assert.Equal(25, requestTools.Count);
        Assert.Equal(0, strictCount);
        Assert.All(requestTools, static tool => Assert.Null(tool!["strict"]));
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
                ["files"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "string"
                    },
                    ["maxItems"] = 4
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
        Assert.False(openAiParameters.ToJsonString().Contains("maxItems", StringComparison.Ordinal));
        Assert.False(openAiParameters.ToJsonString().Contains("format", StringComparison.Ordinal));
        var openAiRequired = openAiParameters["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        Assert.Equal(["path", "timeout", "files", "maybe"], openAiRequired);
        Assert.True(openAiParameters["properties"]!["timeout"]!["anyOf"] is JsonArray);
        Assert.True(openAiParameters["properties"]!["maybe"]!["anyOf"] is JsonArray);

        var codexBody = JsonNode.Parse(await codexRequest.Content!.ReadAsStringAsync())!.AsObject();
        var codexParameters = codexBody["tools"]![0]!["parameters"]!.AsObject();
        Assert.Equal(false, codexParameters["additionalProperties"]?.GetValue<bool>());
        Assert.Equal(4, codexParameters["required"]!.AsArray().Count);
        Assert.False(codexParameters.ToJsonString().Contains("pattern", StringComparison.Ordinal));
        Assert.False(codexParameters.ToJsonString().Contains("format", StringComparison.Ordinal));
        Assert.False(codexParameters.ToJsonString().Contains("maxItems", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateStreamingRequest_Always_Uses_Strict_Mode_For_Codex_Tools()
    {
        var toolSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["title"] = new JsonObject
                {
                    ["type"] = "string"
                },
                ["description"] = new JsonObject
                {
                    ["type"] = "string"
                },
                ["priority"] = new JsonObject
                {
                    ["type"] = "integer"
                }
            },
            ["required"] = new JsonArray("title")
        };

        var request = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                "session-http-codex-strict-tool",
                "gpt-5.4",
                [new QuerySystemPromptBlock("system")],
                [new QueryRequestMessage("user", [new QueryRequestContentBlock("text", Text: "hello")])],
                [new QueryRequestTool("mcp__linear__save_issue", "Create or update a Linear issue.", toolSchema, Strict: false)],
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
        var tool = codexBody["tools"]![0]!.AsObject();
        var parameters = tool["parameters"]!.AsObject();
        var required = parameters["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();

        Assert.Equal(true, tool["strict"]?.GetValue<bool>());
        Assert.Equal(["title", "description", "priority"], required);
    }

    [Fact]
    public async Task CreateStreamingRequest_Normalizes_Malformed_Codex_Object_Schemas()
    {
        var toolSchema = new JsonObject
        {
            ["properties"] = new JsonObject
            {
                ["title"] = new JsonObject
                {
                    ["type"] = "string"
                },
                ["details"] = new JsonObject
                {
                    ["properties"] = new JsonObject
                    {
                        ["status"] = new JsonObject
                        {
                            ["type"] = "string"
                        },
                        ["priority"] = new JsonObject
                        {
                            ["type"] = "integer"
                        }
                    }
                }
            }
        };

        var request = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                "session-http-codex-malformed-schema",
                "gpt-5.4",
                [new QuerySystemPromptBlock("system")],
                [new QueryRequestMessage("user", [new QueryRequestContentBlock("text", Text: "hello")])],
                [new QueryRequestTool("mcp__linear__save_issue", "Create or update a Linear issue.", toolSchema, Strict: false)],
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
        var nested = parameters["properties"]!["details"]!.AsObject();
        var required = parameters["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        var nestedRequired = nested["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();

        Assert.Equal("object", parameters["type"]?.GetValue<string>());
        Assert.Equal(false, parameters["additionalProperties"]?.GetValue<bool>());
        Assert.Equal(["title", "details"], required);
        Assert.Equal("object", nested["type"]?.GetValue<string>());
        Assert.Equal(false, nested["additionalProperties"]?.GetValue<bool>());
        Assert.Equal(["status", "priority"], nestedRequired);
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
    public async Task CreateStreamingRequest_Shapes_Repl_For_Codex_With_Input_Preserved()
    {
        var registry = new ToolRegistry(Environment.CurrentDirectory, new TaskRegistry());
        var repl = Assert.Single(registry.All, static tool => string.Equals(tool.Name, "REPL", StringComparison.Ordinal));

        var request = new QueryModelHttpStreamingRequest(
            new QueryModelRequest(
                "session-http-repl-codex",
                "codexplan",
                [new QuerySystemPromptBlock("system")],
                [new QueryRequestMessage("user", [new QueryRequestContentBlock("text", Text: "hello")])],
                [new QueryRequestTool(repl.Name, repl.Description, repl.InputSchema, Strict: repl.Strict)],
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
        var actionSchema = codexBody["tools"]![0]!["parameters"]!["properties"]!["actions"]!["items"]!.AsObject();
        var actionProperties = actionSchema["properties"]!.AsObject();
        var actionRequired = actionSchema["required"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        var inputSchema = actionProperties["input"]!.AsObject();

        Assert.Equal(["tool", "input"], actionRequired);
        Assert.True(actionProperties.ContainsKey("input"));
        Assert.True(inputSchema["anyOf"] is JsonArray);
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
        Assert.Equal("tooluse-read", assistantToolUse["id"]?.GetValue<string>());
        Assert.Null(assistantToolUse["tool_use_id"]);
        Assert.Equal("note.txt", assistantToolUse["input"]?["file_path"]?.GetValue<string>());

        Assert.Equal("tool_result", userToolResult["type"]?.GetValue<string>());
        Assert.Equal("tooluse-read", userToolResult["tool_use_id"]?.GetValue<string>());
        Assert.Equal("hello", userToolResult["content"]?.GetValue<string>());
        Assert.Null(userToolResult["text"]);
        Assert.Null(userToolResult["name"]);
        Assert.Null(userToolResult["structured_output"]);
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
