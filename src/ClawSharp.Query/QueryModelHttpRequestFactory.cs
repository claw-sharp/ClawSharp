// TS origin: ./services/api/claude.ts
// TS parity status: ports the request-shaping layer for the main query-model HTTP streaming client from the already-built query request snapshot; live transport and streamed response parsing remain intentionally unported.
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace ClawSharp.Query;

public static class QueryModelHttpRequestFactory
{
    public const string AnthropicVersion = "2023-06-01";

    public static HttpRequestMessage CreateStreamingRequest(
        QueryModelHttpClientConfig config,
        QueryModelHttpStreamingRequest request)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(request);

        var target = $"{config.BaseUrl.TrimEnd('/')}/v1/messages";
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, target)
        {
            Content = new StringContent(
                CreateRequestBody(request).ToJsonString(),
                Encoding.UTF8,
                "application/json")
        };

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

        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return httpRequest;
    }

    public static JsonObject CreateRequestBody(QueryModelHttpStreamingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = new JsonObject
        {
            ["model"] = request.Request.Model,
            ["stream"] = true,
            ["messages"] = new JsonArray(request.Request.Messages.Select(ToJson).ToArray()),
            ["system"] = new JsonArray(request.Request.System.Select(ToJson).ToArray()),
            ["tools"] = new JsonArray(request.Request.Tools.Select(ToJson).ToArray()),
            ["output_config"] = ToJson(request.Request.OutputConfig)
        };

        if (request.Request.MaxTokens is not null)
        {
            body["max_tokens"] = request.Request.MaxTokens.Value;
        }

        if (request.Request.Thinking is not null)
        {
            body["thinking"] = ToJson(request.Request.Thinking);
        }

        return body;
    }

    private static JsonObject ToJson(QueryRequestMessage message)
    {
        return new JsonObject
        {
            ["role"] = message.Role,
            ["content"] = new JsonArray(message.Content.Select(ToJson).ToArray())
        };
    }

    private static JsonObject ToJson(QueryRequestContentBlock block)
    {
        var json = new JsonObject
        {
            ["type"] = block.Type
        };

        if (block.Text is not null)
        {
            json["text"] = block.Text;
        }

        if (block.Name is not null)
        {
            json["name"] = block.Name;
        }

        if (block.ToolUseId is not null)
        {
            json["tool_use_id"] = block.ToolUseId;
        }

        if (block.Input is not null)
        {
            json["input"] = JsonNode.Parse(block.Input);
        }

        if (block.StructuredOutput is not null)
        {
            json["structured_output"] = JsonNode.Parse(block.StructuredOutput);
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

    private static JsonObject ToJson(QuerySystemPromptBlock block)
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

    private static JsonObject ToJson(QueryRequestTool tool)
    {
        var json = new JsonObject
        {
            ["name"] = tool.Name,
            ["description"] = tool.Description
        };

        if (tool.InputSchema is not null)
        {
            json["input_schema"] = tool.InputSchema.DeepClone();
        }

        if (tool.Strict)
        {
            json["strict"] = true;
        }

        return json;
    }

    private static JsonObject ToJson(QueryRequestOutputConfig outputConfig)
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

    private static JsonObject ToJson(QueryThinkingConfig thinking)
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
}
