// TS parity status: ports the request-shaping layer for Anthropic, OpenAI-compatible, and Codex-backed model transports from the shared query request snapshot; Bedrock/Vertex/Foundry still ride the Anthropic-shaped path until their auth/signing layers are ported.
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Query;

public static class QueryModelHttpRequestFactory
{
    public const string AnthropicVersion = "2023-06-01";
    public const int AnthropicStrictToolLimit = 20;

    public static HttpRequestMessage CreateStreamingRequest(
        QueryModelHttpClientConfig config,
        QueryModelHttpStreamingRequest request)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(request);

        return config.TransportKind switch
        {
            ModelTransportKind.OpenAiChatCompletions => CreateOpenAiStreamingRequest(config, request),
            ModelTransportKind.CodexResponses => CreateCodexStreamingRequest(config, request),
            _ => CreateAnthropicStreamingRequest(config, request)
        };
    }

    public static JsonObject CreateRequestBody(QueryModelHttpStreamingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return CreateAnthropicRequestBody(request);
    }

    private static HttpRequestMessage CreateAnthropicStreamingRequest(
        QueryModelHttpClientConfig config,
        QueryModelHttpStreamingRequest request)
    {
        var target = $"{config.BaseUrl.TrimEnd('/')}/v1/messages";
        var body = CreateAnthropicRequestBody(request);
        var httpRequest = CreateJsonRequest(target, body);

        httpRequest.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
        if (!string.IsNullOrWhiteSpace(config.ApiKey))
        {
            httpRequest.Headers.TryAddWithoutValidation("x-api-key", config.ApiKey);
        }
        else if (!string.IsNullOrWhiteSpace(config.AuthToken))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.AuthToken);
        }

        if (request.Request.Betas.Count > 0)
        {
            httpRequest.Headers.TryAddWithoutValidation("anthropic-beta", string.Join(",", request.Request.Betas));
        }

        MaybeDumpDebugRequest("anthropic", httpRequest, body);
        return httpRequest;
    }

    private static HttpRequestMessage CreateOpenAiStreamingRequest(
        QueryModelHttpClientConfig config,
        QueryModelHttpStreamingRequest request)
    {
        var target = BuildOpenAiChatCompletionsUrl(config, request.Request.Model);
        var body = CreateOpenAiRequestBody(config, request);
        var httpRequest = CreateJsonRequest(target, body);
        ApplyAdditionalHeaders(httpRequest, config.AdditionalHeaders);

        var secret = config.ApiKey ?? config.AuthToken;
        if (!string.IsNullOrWhiteSpace(secret))
        {
            if (IsAzureBaseUrl(config.BaseUrl) && !string.IsNullOrWhiteSpace(config.ApiKey))
            {
                httpRequest.Headers.TryAddWithoutValidation("api-key", config.ApiKey);
            }
            else
            {
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
            }
        }

        MaybeDumpDebugRequest("openai", httpRequest, body);
        return httpRequest;
    }

    private static HttpRequestMessage CreateCodexStreamingRequest(
        QueryModelHttpClientConfig config,
        QueryModelHttpStreamingRequest request)
    {
        var target = $"{config.BaseUrl.TrimEnd('/')}/responses";
        var body = CreateCodexRequestBody(request);
        var httpRequest = CreateCodexJsonRequest(target, body);
        ApplyAdditionalHeaders(httpRequest, config.AdditionalHeaders);

        if (!string.IsNullOrWhiteSpace(config.ApiKey))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        }

        if (!string.IsNullOrWhiteSpace(config.AccountId))
        {
            httpRequest.Headers.TryAddWithoutValidation("chatgpt-account-id", config.AccountId);
        }

        MaybeDumpDebugRequest("codex", httpRequest, body);
        return httpRequest;
    }

    private static HttpRequestMessage CreateJsonRequest(string target, JsonObject body)
    {
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, target)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };

        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return httpRequest;
    }

    private static HttpRequestMessage CreateCodexJsonRequest(string target, JsonObject body)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(body.ToJsonString()));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json");

        return new HttpRequestMessage(HttpMethod.Post, target)
        {
            Content = content
        };
    }

    private static void MaybeDumpDebugRequest(string provider, HttpRequestMessage request, JsonObject body)
    {
        var dumpPath = Environment.GetEnvironmentVariable("CLAWSHARP_DEBUG_HTTP_DUMP");
        if (string.IsNullOrWhiteSpace(dumpPath))
        {
            return;
        }

        var root = new JsonObject
        {
            ["provider"] = provider,
            ["method"] = request.Method.Method,
            ["uri"] = request.RequestUri?.ToString(),
            ["headers"] = new JsonObject(),
            ["contentHeaders"] = new JsonObject(),
            ["body"] = body.DeepClone()
        };

        foreach (var header in request.Headers)
        {
            root["headers"]![header.Key] = string.Join(", ", header.Value);
        }

        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers)
            {
                root["contentHeaders"]![header.Key] = string.Join(", ", header.Value);
            }
        }

        var directory = Path.GetDirectoryName(dumpPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            dumpPath,
            root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            }));
    }

    private static JsonObject CreateAnthropicRequestBody(QueryModelHttpStreamingRequest request)
    {
        var body = new JsonObject
        {
            ["model"] = request.Request.Model,
            ["stream"] = true,
            ["messages"] = new JsonArray(request.Request.Messages.Select(ToAnthropicJson).ToArray()),
            ["system"] = new JsonArray(request.Request.System.Select(ToAnthropicJson).ToArray()),
            ["tools"] = new JsonArray(BuildAnthropicTools(request.Request.Tools).ToArray()),
            ["output_config"] = ToAnthropicJson(request.Request.OutputConfig)
        };

        if (request.Request.MaxTokens is not null)
        {
            body["max_tokens"] = request.Request.MaxTokens.Value;
        }

        if (request.Request.Thinking is not null)
        {
            body["thinking"] = ToAnthropicJson(request.Request.Thinking);
        }

        return body;
    }

    private static IEnumerable<JsonObject> BuildAnthropicTools(IReadOnlyList<QueryRequestTool> tools)
    {
        foreach (var tool in tools)
        {
            yield return ToAnthropicJson(tool, emitStrict: false);
        }
    }

    private static JsonObject CreateOpenAiRequestBody(
        QueryModelHttpClientConfig config,
        QueryModelHttpStreamingRequest request)
    {
        var body = new JsonObject
        {
            ["model"] = request.Request.Model,
            ["stream"] = true,
            ["messages"] = new JsonArray(ConvertToOpenAiMessages(request.Request).ToArray())
        };

        if (request.Request.MaxTokens is not null)
        {
            if (config.ProviderKind == ApiProviderKind.GitHub)
            {
                body["max_tokens"] = request.Request.MaxTokens.Value;
            }
            else
            {
                body["max_completion_tokens"] = request.Request.MaxTokens.Value;
            }
        }

        if (config.ProviderKind != ApiProviderKind.GitHub &&
            config.ProviderKind != ApiProviderKind.Gemini &&
            !ProviderRuntimeResolver.IsLocalProviderUrl(config.BaseUrl))
        {
            body["stream_options"] = new JsonObject
            {
                ["include_usage"] = true
            };
        }

        if (request.Request.Tools.Count > 0)
        {
            body["tools"] = new JsonArray(request.Request.Tools.Select(ToOpenAiToolJson).ToArray());
        }

        return body;
    }

    private static JsonObject CreateCodexRequestBody(QueryModelHttpStreamingRequest request)
    {
        var body = new JsonObject
        {
            ["model"] = request.Request.Model,
            ["input"] = new JsonArray(ConvertToCodexInput(request.Request).ToArray()),
            ["store"] = false,
            ["stream"] = true
        };

        var instructions = string.Join(
            "\n\n",
            request.Request.System
                .Select(block => block.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text)));
        if (!string.IsNullOrWhiteSpace(instructions))
        {
            body["instructions"] = instructions;
        }

        if (request.Request.Tools.Count > 0)
        {
            body["tools"] = new JsonArray(request.Request.Tools.Select(ToCodexToolJson).ToArray());
            body["parallel_tool_calls"] = true;
            body["tool_choice"] = "auto";
        }

        return body;
    }

    private static IEnumerable<JsonObject> ConvertToOpenAiMessages(QueryModelRequest request)
    {
        var systemText = string.Join(
            "\n\n",
            request.System
                .Select(block => block.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text)));
        if (!string.IsNullOrWhiteSpace(systemText))
        {
            yield return new JsonObject
            {
                ["role"] = "system",
                ["content"] = systemText
            };
        }

        foreach (var message in request.Messages)
        {
            if (string.Equals(message.Role, "user", StringComparison.Ordinal))
            {
                var toolResults = message.Content
                    .Where(block => string.Equals(block.Type, "tool_result", StringComparison.Ordinal))
                    .ToArray();
                foreach (var toolResult in toolResults)
                {
                    yield return new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = toolResult.ToolUseId ?? "unknown",
                        ["content"] = toolResult.Text ?? string.Empty
                    };
                }

                var userText = string.Join(
                    "\n",
                    message.Content
                        .Where(block => string.Equals(block.Type, "text", StringComparison.Ordinal))
                        .Select(block => block.Text)
                        .Where(text => !string.IsNullOrWhiteSpace(text)));

                var imageParts = message.Content
                    .Where(block =>
                        string.Equals(block.Type, "image", StringComparison.Ordinal) &&
                        block.ImageSource is not null &&
                        !string.IsNullOrWhiteSpace(block.ImageSource.MediaType) &&
                        !string.IsNullOrWhiteSpace(block.ImageSource.Data))
                    .Select(
                        block => new JsonObject
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new JsonObject
                            {
                                ["url"] = BuildDataUrl(block.ImageSource!.MediaType, block.ImageSource.Data)
                            }
                        })
                    .Cast<JsonNode>()
                    .ToArray();

                if (imageParts.Length > 0)
                {
                    var userParts = new List<JsonNode>();
                    if (!string.IsNullOrWhiteSpace(userText))
                    {
                        userParts.Add(new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = userText
                        });
                    }

                    userParts.AddRange(imageParts);
                    yield return new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = new JsonArray(userParts.ToArray())
                    };
                }
                else if (!string.IsNullOrWhiteSpace(userText))
                {
                    yield return new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = userText
                    };
                }

                continue;
            }

            if (string.Equals(message.Role, "assistant", StringComparison.Ordinal))
            {
                var assistantMessage = new JsonObject
                {
                    ["role"] = "assistant"
                };
                var assistantText = string.Join(
                    "\n",
                    message.Content
                        .Where(block => string.Equals(block.Type, "text", StringComparison.Ordinal))
                        .Select(block => block.Text)
                        .Where(text => !string.IsNullOrWhiteSpace(text)));
                if (!string.IsNullOrWhiteSpace(assistantText))
                {
                    assistantMessage["content"] = assistantText;
                }

                var toolCalls = message.Content
                    .Where(block => string.Equals(block.Type, "tool_use", StringComparison.Ordinal))
                    .Select(
                        block =>
                        {
                            var toolCall = new JsonObject
                            {
                                ["id"] = block.ToolUseId ?? $"call_{Guid.NewGuid():N}",
                                ["type"] = "function",
                                ["function"] = new JsonObject
                                {
                                    ["name"] = block.Name ?? "tool",
                                    ["arguments"] = block.Input ?? "{}"
                                }
                            };
                            return toolCall;
                        })
                    .ToArray();
                if (toolCalls.Length > 0)
                {
                    assistantMessage["tool_calls"] = new JsonArray(toolCalls);
                }

                if (assistantMessage["content"] is not null || assistantMessage["tool_calls"] is not null)
                {
                    yield return assistantMessage;
                }
            }
        }
    }

    private static IEnumerable<JsonObject> ConvertToCodexInput(QueryModelRequest request)
    {
        if (request.PreviousResponseItems?.Count > 0)
        {
            foreach (var rawItem in request.PreviousResponseItems)
            {
                if (string.IsNullOrWhiteSpace(rawItem))
                {
                    continue;
                }

                var parsed = JsonNode.Parse(rawItem)?.AsObject();
                if (parsed is not null)
                {
                    yield return parsed;
                }
            }
        }

        foreach (var message in request.Messages)
        {
            if (string.Equals(message.Role, "user", StringComparison.Ordinal))
            {
                var contentParts = message.Content
                    .Where(block => string.Equals(block.Type, "text", StringComparison.Ordinal))
                    .Select(
                        block => new JsonObject
                        {
                            ["type"] = "input_text",
                            ["text"] = block.Text ?? string.Empty
                        })
                    .Cast<JsonNode>()
                    .Concat(
                        message.Content
                            .Where(block =>
                                string.Equals(block.Type, "image", StringComparison.Ordinal) &&
                                block.ImageSource is not null &&
                                !string.IsNullOrWhiteSpace(block.ImageSource.MediaType) &&
                                !string.IsNullOrWhiteSpace(block.ImageSource.Data))
                            .Select(
                                block => (JsonNode)new JsonObject
                                {
                                    ["type"] = "input_image",
                                    ["image_url"] = BuildDataUrl(block.ImageSource!.MediaType, block.ImageSource.Data)
                                }))
                    .ToArray();
                if (contentParts.Length > 0)
                {
                    yield return new JsonObject
                    {
                        ["type"] = "message",
                        ["role"] = "user",
                        ["content"] = new JsonArray(contentParts)
                    };
                }

                foreach (var toolResult in message.Content.Where(block => string.Equals(block.Type, "tool_result", StringComparison.Ordinal)))
                {
                    yield return new JsonObject
                    {
                        ["type"] = "function_call_output",
                        ["call_id"] = NormalizeCodexCallId(toolResult.ToolUseId).CallId,
                        ["output"] = toolResult.Text ?? string.Empty
                    };
                }

                continue;
            }

            if (string.Equals(message.Role, "assistant", StringComparison.Ordinal))
            {
                var textParts = message.Content
                    .Where(block => string.Equals(block.Type, "text", StringComparison.Ordinal))
                    .Select(
                        block => new JsonObject
                        {
                            ["type"] = "output_text",
                            ["text"] = block.Text ?? string.Empty
                        })
                    .Cast<JsonNode>()
                    .ToArray();
                if (textParts.Length > 0)
                {
                    yield return new JsonObject
                    {
                        ["type"] = "message",
                        ["role"] = "assistant",
                        ["content"] = new JsonArray(textParts)
                    };
                }

                foreach (var toolUse in message.Content.Where(block => string.Equals(block.Type, "tool_use", StringComparison.Ordinal)))
                {
                    var ids = NormalizeCodexCallId(toolUse.ToolUseId);
                    var itemId = string.IsNullOrWhiteSpace(toolUse.ToolCallItemId)
                        ? ids.Id
                        : toolUse.ToolCallItemId;
                    yield return new JsonObject
                    {
                        ["type"] = "function_call",
                        ["id"] = itemId,
                        ["call_id"] = ids.CallId,
                        ["name"] = toolUse.Name ?? "tool",
                        ["arguments"] = toolUse.Input ?? "{}"
                    };
                }
            }
        }
    }

    private static (string Id, string CallId) NormalizeCodexCallId(string? toolUseId)
    {
        var value = string.IsNullOrWhiteSpace(toolUseId)
            ? "unknown"
            : toolUseId.Trim();
        if (value.StartsWith("call_", StringComparison.Ordinal))
        {
            return ($"fc_{value["call_".Length..]}", value);
        }

        if (value.StartsWith("fc_", StringComparison.Ordinal))
        {
            return (value, $"call_{value["fc_".Length..]}");
        }

        return ($"fc_{value}", value);
    }

    private static string BuildDataUrl(string mediaType, string base64Data)
    {
        return $"data:{mediaType};base64,{base64Data}";
    }

    private static string BuildOpenAiChatCompletionsUrl(QueryModelHttpClientConfig config, string model)
    {
        if (!IsAzureBaseUrl(config.BaseUrl))
        {
            return $"{config.BaseUrl.TrimEnd('/')}/chat/completions";
        }

        var apiVersion = string.IsNullOrWhiteSpace(config.ApiVersion)
            ? "2024-12-01-preview"
            : config.ApiVersion;
        var baseUrl = config.BaseUrl.TrimEnd('/');

        if (baseUrl.Contains("/deployments/", StringComparison.OrdinalIgnoreCase))
        {
            return $"{baseUrl}/chat/completions?api-version={Uri.EscapeDataString(apiVersion)}";
        }

        baseUrl = baseUrl
            .Replace("/openai/v1", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("/v1", string.Empty, StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');

        return $"{baseUrl}/openai/deployments/{Uri.EscapeDataString(model)}/chat/completions?api-version={Uri.EscapeDataString(apiVersion)}";
    }

    private static bool IsAzureBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        return host.EndsWith(".azure.com", StringComparison.Ordinal) &&
               (host.Contains("cognitiveservices", StringComparison.Ordinal) ||
                host.Contains("openai", StringComparison.Ordinal) ||
                host.Contains("services.ai", StringComparison.Ordinal));
    }

    private static void ApplyAdditionalHeaders(HttpRequestMessage httpRequest, IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null)
        {
            return;
        }

        foreach (var pair in headers)
        {
            httpRequest.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
        }
    }

    private static JsonObject ToAnthropicJson(QueryRequestMessage message)
    {
        return new JsonObject
        {
            ["role"] = message.Role,
            ["content"] = new JsonArray(message.Content.Select(ToAnthropicJson).ToArray())
        };
    }

    private static JsonObject ToAnthropicJson(QueryRequestContentBlock block)
    {
        var json = new JsonObject
        {
            ["type"] = block.Type
        };

        if (block.Text is not null)
        {
            json[string.Equals(block.Type, "tool_result", StringComparison.Ordinal) ? "content" : "text"] = block.Text;
        }

        if (block.Name is not null &&
            !string.Equals(block.Type, "tool_result", StringComparison.Ordinal))
        {
            json["name"] = block.Name;
        }

        if (block.ToolUseId is not null)
        {
            json[string.Equals(block.Type, "tool_use", StringComparison.Ordinal) ? "id" : "tool_use_id"] = block.ToolUseId;
        }

        if (block.Input is not null)
        {
            json["input"] = JsonNode.Parse(block.Input);
        }

        if (block.StructuredOutput is not null &&
            !string.Equals(block.Type, "tool_result", StringComparison.Ordinal))
        {
            json["structured_output"] = JsonNode.Parse(block.StructuredOutput);
        }

        if (block.ImageSource is not null &&
            string.Equals(block.Type, "image", StringComparison.Ordinal))
        {
            json["source"] = new JsonObject
            {
                ["type"] = block.ImageSource.Type,
                ["media_type"] = block.ImageSource.MediaType,
                ["data"] = block.ImageSource.Data
            };
        }

        if (block.CacheControl is not null)
        {
            json["cache_control"] = new JsonObject
            {
                ["type"] = block.CacheControl.Type,
                ["scope"] = block.CacheControl.Scope
            };
        }

        return json;
    }

    private static JsonObject ToAnthropicJson(QuerySystemPromptBlock block)
    {
        var json = new JsonObject
        {
            ["type"] = "text",
            ["text"] = block.Text
        };

        if (block.CacheControl is not null)
        {
            json["cache_control"] = new JsonObject
            {
                ["type"] = block.CacheControl.Type,
                ["scope"] = block.CacheControl.Scope
            };
        }

        return json;
    }

    private static JsonObject ToAnthropicJson(QueryRequestTool tool, bool emitStrict = true)
    {
        var json = new JsonObject();
        if (!string.IsNullOrEmpty(tool.Type))
        {
            json["type"] = tool.Type;
        }

        json["name"] = tool.Name;
        json["description"] = tool.Description;

        if (tool.InputSchema is not null)
        {
            json["input_schema"] = StripAnthropicUnsupportedSchemaKeywords(tool.InputSchema);
        }

        if (tool.Strict && emitStrict)
        {
            json["strict"] = true;
        }

        return json;
    }

    private static JsonObject ToAnthropicJson(QueryRequestOutputConfig outputConfig)
    {
        var json = new JsonObject();
        if (outputConfig.TaskBudget is not null)
        {
            json["task_budget"] = new JsonObject
            {
                ["type"] = "tokens",
                ["total"] = outputConfig.TaskBudget.Total,
                ["remaining"] = outputConfig.TaskBudget.Remaining
            };
        }

        return json;
    }

    private static JsonObject ToAnthropicJson(QueryThinkingConfig thinking)
    {
        var json = new JsonObject
        {
            ["type"] = thinking.Type
        };

        if (thinking.BudgetTokens is not null)
        {
            json["budget_tokens"] = thinking.BudgetTokens.Value;
        }

        return json;
    }

    private static JsonObject ToOpenAiToolJson(QueryRequestTool tool)
    {
        var function = new JsonObject
        {
            ["name"] = tool.Name,
            ["description"] = tool.Description,
            ["parameters"] = tool.Strict
                ? OpenAiSchemaSanitizer.EnforceOpenAiStrictSchema(tool.InputSchema)
                : OpenAiSchemaSanitizer.SanitizeForOpenAiCompat(tool.InputSchema)
        };
        var json = new JsonObject
        {
            ["type"] = "function",
            ["function"] = function
        };

        if (tool.Strict)
        {
            function["strict"] = true;
        }

        return json;
    }

    private static JsonObject ToCodexToolJson(QueryRequestTool tool)
    {
        var json = new JsonObject
        {
            ["type"] = "function",
            ["name"] = tool.Name,
            ["description"] = tool.Description,
            ["parameters"] = OpenAiSchemaSanitizer.EnforceCodexStrictSchema(tool.InputSchema),
            ["strict"] = true
        };

        return json;
    }

    private static JsonNode? StripAnthropicUnsupportedSchemaKeywords(JsonNode? node)
    {
        return node switch
        {
            JsonObject obj => StripAnthropicUnsupportedSchemaKeywords(obj),
            JsonArray array => new JsonArray(array.Select(StripAnthropicUnsupportedSchemaKeywords).ToArray()),
            null => null,
            _ => node.DeepClone()
        };
    }

    private static JsonObject StripAnthropicUnsupportedSchemaKeywords(JsonObject obj)
    {
        var result = new JsonObject();
        foreach (var pair in obj)
        {
            if (string.Equals(pair.Key, "maxItems", StringComparison.Ordinal) ||
                string.Equals(pair.Key, "minimum", StringComparison.Ordinal) ||
                string.Equals(pair.Key, "maximum", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(pair.Key, "minItems", StringComparison.Ordinal) &&
                pair.Value is JsonValue minItemsValue &&
                minItemsValue.TryGetValue<int>(out var minItems) &&
                minItems > 1)
            {
                result[pair.Key] = 1;
                continue;
            }

            result[pair.Key] = StripAnthropicUnsupportedSchemaKeywords(pair.Value);
        }

        return result;
    }
}
