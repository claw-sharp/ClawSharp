// TS parity status: ports the raw SSE transport layer beneath the main query-model HTTP streaming client for Anthropic, OpenAI-compatible, and Codex-backed providers by normalizing provider-specific stream payloads into Anthropic-style stream events before higher-level parsing.
using System.Text;
using System.Text.Json.Nodes;
using System.Net.Http.Headers;
using ClawSharp.Core;

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

        switch (config.TransportKind)
        {
            case ModelTransportKind.OpenAiChatCompletions:
            {
                var normalizer = new OpenAiStreamNormalizer(request.Request.Model);
                await foreach (var frame in ReadSseFramesAsync(reader, cancellationToken))
                {
                    if (string.IsNullOrWhiteSpace(frame.Data) ||
                        string.Equals(frame.Data.Trim(), "[DONE]", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var payload = JsonNode.Parse(frame.Data) ?? throw new InvalidOperationException("Streamed model payload parsed to null.");
                    foreach (var normalized in normalizer.Normalize(payload))
                    {
                        yield return normalized;
                    }
                }

                foreach (var finalEvent in normalizer.Complete())
                {
                    yield return finalEvent;
                }

                break;
            }
            case ModelTransportKind.CodexResponses:
            {
                var normalizer = new CodexStreamNormalizer(request.Request.Model);
                await foreach (var frame in ReadSseFramesAsync(reader, cancellationToken))
                {
                    foreach (var normalized in normalizer.Normalize(frame))
                    {
                        yield return normalized;
                    }
                }

                foreach (var finalEvent in normalizer.Complete())
                {
                    yield return finalEvent;
                }

                break;
            }
            default:
            {
                await foreach (var frame in ReadSseFramesAsync(reader, cancellationToken))
                {
                    if (TryParsePayload(frame.Data, out var payload))
                    {
                        yield return payload;
                    }
                }

                break;
            }
        }
    }

    private static async IAsyncEnumerable<SseFrame> ReadSseFramesAsync(
        StreamReader reader,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? eventName = null;
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
                if (dataBuilder.Length > 0)
                {
                    yield return new SseFrame(eventName, dataBuilder.ToString().TrimEnd('\r', '\n'));
                    eventName = null;
                    dataBuilder.Clear();
                }

                continue;
            }

            if (line.StartsWith(':'))
            {
                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventName = line["event:".Length..].Trim();
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

        if (dataBuilder.Length > 0)
        {
            yield return new SseFrame(eventName, dataBuilder.ToString().TrimEnd('\r', '\n'));
        }
    }

    private static bool TryParsePayload(string rawPayload, out JsonNode payload)
    {
        payload = null!;

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

    private sealed record SseFrame(string? EventName, string Data);

    private sealed class OpenAiStreamNormalizer
    {
        private readonly string _model;
        private readonly Dictionary<int, ActiveToolCall> _activeToolCalls = [];
        private readonly List<JsonNode> _queuedFinalEvents = [];
        private int _contentBlockIndex;
        private bool _messageStarted;
        private bool _messageStopped;
        private bool _processedFinishReason;
        private bool _hasEmittedFinalUsage;
        private string? _lastStopReason;
        private int? _activeTextBlockIndex;

        public OpenAiStreamNormalizer(string model)
        {
            _model = model;
        }

        public IEnumerable<JsonNode> Normalize(JsonNode payload)
        {
            if (!_messageStarted)
            {
                _messageStarted = true;
                yield return new JsonObject
                {
                    ["type"] = "message_start",
                    ["message"] = new JsonObject
                    {
                        ["id"] = $"msg_{Guid.NewGuid():N}",
                        ["type"] = "message",
                        ["role"] = "assistant",
                        ["content"] = new JsonArray(),
                        ["model"] = _model,
                        ["stop_reason"] = null,
                        ["stop_sequence"] = null,
                        ["usage"] = new JsonObject
                        {
                            ["input_tokens"] = 0,
                            ["output_tokens"] = 0
                        }
                    }
                };
            }

            if (payload["error"] is not null)
            {
                var errorObj = payload["error"]?.AsObject();
                var message = errorObj?["message"]?.GetValue<string>() ?? payload["error"]?.ToString() ?? "Unknown streaming API error.";
                yield return new JsonObject
                {
                    ["type"] = "error",
                    ["error"] = new JsonObject
                    {
                        ["type"] = "api_error",
                        ["message"] = message
                    }
                };
                yield break;
            }

            var chunkUsage = ConvertUsage(payload["usage"]);
            var choices = payload["choices"]?.AsArray();
            if (choices is null)
            {
                if (!_hasEmittedFinalUsage &&
                    chunkUsage is not null &&
                    !string.IsNullOrWhiteSpace(_lastStopReason))
                {
                    _hasEmittedFinalUsage = true;
                    yield return new JsonObject
                    {
                        ["type"] = "message_delta",
                        ["delta"] = new JsonObject
                        {
                            ["stop_reason"] = _lastStopReason,
                            ["stop_sequence"] = null
                        },
                        ["usage"] = chunkUsage
                    };
                }
                yield break;
            }

            foreach (var choiceNode in choices)
            {
                var choice = choiceNode?.AsObject();
                var delta = choice?["delta"]?.AsObject();
                if (delta is not null && delta["content"] is not null)
                {
                    foreach (var update in StartTextBlockIfNeeded())
                    {
                        yield return update;
                    }

                    yield return new JsonObject
                    {
                        ["type"] = "content_block_delta",
                        ["index"] = _activeTextBlockIndex,
                        ["delta"] = new JsonObject
                        {
                            ["type"] = "text_delta",
                            ["text"] = delta["content"]?.GetValue<string>() ?? string.Empty
                        }
                    };
                }

                var toolCalls = delta?["tool_calls"]?.AsArray();
                if (toolCalls is not null)
                {
                    foreach (var toolCallNode in toolCalls)
                    {
                        var toolCall = toolCallNode?.AsObject();
                        var index = toolCall?["index"]?.GetValue<int>() ?? 0;
                        var id = toolCall?["id"]?.GetValue<string>();
                        var function = toolCall?["function"]?.AsObject();
                        var name = function?["name"]?.GetValue<string>();
                        var arguments = function?["arguments"]?.GetValue<string>() ?? string.Empty;

                        if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name))
                        {
                            foreach (var update in CloseActiveTextBlock())
                            {
                                yield return update;
                            }

                            var toolBlockIndex = _contentBlockIndex++;
                            _activeToolCalls[index] = new ActiveToolCall(toolBlockIndex, id!, name!, arguments);
                            yield return new JsonObject
                            {
                                ["type"] = "content_block_start",
                                ["index"] = toolBlockIndex,
                                ["content_block"] = new JsonObject
                                {
                                    ["type"] = "tool_use",
                                    ["id"] = id,
                                    ["name"] = name,
                                    ["input"] = new JsonObject()
                                }
                            };

                            if (!string.IsNullOrWhiteSpace(arguments))
                            {
                                yield return CreateInputJsonDelta(toolBlockIndex, arguments);
                            }

                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(arguments) &&
                            _activeToolCalls.TryGetValue(index, out var activeToolCall))
                        {
                            activeToolCall.JsonBuffer.Append(arguments);
                            yield return CreateInputJsonDelta(activeToolCall.Index, arguments);
                        }
                    }
                }

                var finishReason = choice?["finish_reason"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(finishReason) && !_processedFinishReason)
                {
                    _processedFinishReason = true;

                    foreach (var update in CloseActiveTextBlock())
                    {
                        yield return update;
                    }

                    foreach (var toolCall in _activeToolCalls.Values)
                    {
                        yield return new JsonObject
                        {
                            ["type"] = "content_block_stop",
                            ["index"] = toolCall.Index
                        };
                    }

                    _activeToolCalls.Clear();

                    _lastStopReason = finishReason switch
                    {
                        "tool_calls" => "tool_use",
                        "length" => "max_tokens",
                        _ => "end_turn"
                    };
                    var messageDelta = new JsonObject
                    {
                        ["type"] = "message_delta",
                        ["delta"] = new JsonObject
                        {
                            ["stop_reason"] = _lastStopReason,
                            ["stop_sequence"] = null
                        }
                    };
                    if (chunkUsage is not null)
                    {
                        messageDelta["usage"] = chunkUsage;
                        _hasEmittedFinalUsage = true;
                    }

                    yield return messageDelta;
                }
            }

            if (!_hasEmittedFinalUsage &&
                chunkUsage is not null &&
                choices.Count == 0 &&
                !string.IsNullOrWhiteSpace(_lastStopReason))
            {
                _hasEmittedFinalUsage = true;
                yield return new JsonObject
                {
                    ["type"] = "message_delta",
                    ["delta"] = new JsonObject
                    {
                        ["stop_reason"] = _lastStopReason,
                        ["stop_sequence"] = null
                    },
                    ["usage"] = chunkUsage
                };
            }
        }

        public IEnumerable<JsonNode> Complete()
        {
            foreach (var update in CloseActiveTextBlock())
            {
                yield return update;
            }

            foreach (var toolCall in _activeToolCalls.Values)
            {
                yield return new JsonObject
                {
                    ["type"] = "content_block_stop",
                    ["index"] = toolCall.Index
                };
            }

            _activeToolCalls.Clear();

            if (!_messageStopped)
            {
                _messageStopped = true;
                yield return new JsonObject
                {
                    ["type"] = "message_stop"
                };
            }
        }

        private IEnumerable<JsonNode> StartTextBlockIfNeeded()
        {
            if (_activeTextBlockIndex is not null)
            {
                yield break;
            }

            _activeTextBlockIndex = _contentBlockIndex++;
            yield return new JsonObject
            {
                ["type"] = "content_block_start",
                ["index"] = _activeTextBlockIndex,
                ["content_block"] = new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = string.Empty
                }
            };
        }

        private IEnumerable<JsonNode> CloseActiveTextBlock()
        {
            if (_activeTextBlockIndex is null)
            {
                yield break;
            }

            yield return new JsonObject
            {
                ["type"] = "content_block_stop",
                ["index"] = _activeTextBlockIndex
            };
            _activeTextBlockIndex = null;
        }

        private static JsonObject? ConvertUsage(JsonNode? usageNode)
        {
            var usage = usageNode?.AsObject();
            if (usage is null)
            {
                return null;
            }

            var inputTokens = usage["prompt_tokens"]?.GetValue<int?>() ?? 0;
            var outputTokens = usage["completion_tokens"]?.GetValue<int?>() ?? 0;
            if (inputTokens == 0 && outputTokens == 0)
            {
                return null;
            }

            return new JsonObject
            {
                ["input_tokens"] = inputTokens,
                ["output_tokens"] = outputTokens
            };
        }

        private static JsonObject CreateInputJsonDelta(int blockIndex, string partialJson)
        {
            return new JsonObject
            {
                ["type"] = "content_block_delta",
                ["index"] = blockIndex,
                ["delta"] = new JsonObject
                {
                    ["type"] = "input_json_delta",
                    ["partial_json"] = partialJson
                }
            };
        }

        private sealed record ActiveToolCall(int Index, string Id, string Name, string InitialArguments)
        {
            public StringBuilder JsonBuffer { get; } = new StringBuilder(InitialArguments);
        }
    }

    private sealed class CodexStreamNormalizer
    {
        private readonly string _model;
        private readonly Dictionary<string, ToolBlockState> _toolBlocksByItemId = [];
        private int _nextContentBlockIndex;
        private int? _activeTextBlockIndex;
        private bool _started;
        private bool _stopped;
        private bool _sawToolUse;
        private JsonObject? _finalResponse;

        public CodexStreamNormalizer(string model)
        {
            _model = model;
        }

        public IEnumerable<JsonNode> Normalize(SseFrame frame)
        {
            if (!_started)
            {
                _started = true;
                yield return new JsonObject
                {
                    ["type"] = "message_start",
                    ["message"] = new JsonObject
                    {
                        ["id"] = $"msg_{Guid.NewGuid():N}",
                        ["type"] = "message",
                        ["role"] = "assistant",
                        ["content"] = new JsonArray(),
                        ["model"] = _model,
                        ["stop_reason"] = null,
                        ["stop_sequence"] = null,
                        ["usage"] = new JsonObject
                        {
                            ["input_tokens"] = 0,
                            ["output_tokens"] = 0
                        }
                    }
                };
            }

            if (string.IsNullOrWhiteSpace(frame.Data) ||
                string.Equals(frame.Data.Trim(), "[DONE]", StringComparison.Ordinal))
            {
                yield break;
            }

            var payload = JsonNode.Parse(frame.Data)?.AsObject();
            if (payload is null)
            {
                yield break;
            }

            switch (frame.EventName)
            {
                case "response.output_item.added":
                {
                    var item = payload["item"]?.AsObject();
                    if (item?["type"]?.GetValue<string>() == "function_call")
                    {
                        foreach (var update in CloseActiveTextBlock())
                        {
                            yield return update;
                        }

                        var blockIndex = _nextContentBlockIndex++;
                        var toolUseId = item["call_id"]?.GetValue<string>() ??
                                        item["id"]?.GetValue<string>() ??
                                        $"call_{blockIndex}";
                        var itemId = item["id"]?.GetValue<string>() ?? toolUseId;
                        _toolBlocksByItemId[itemId] = new ToolBlockState(blockIndex, toolUseId);
                        _sawToolUse = true;

                        yield return new JsonObject
                        {
                            ["type"] = "content_block_start",
                            ["index"] = blockIndex,
                            ["content_block"] = new JsonObject
                            {
                                ["type"] = "tool_use",
                                ["id"] = toolUseId,
                                ["name"] = item["name"]?.GetValue<string>() ?? "tool",
                                ["input"] = new JsonObject()
                            }
                        };

                        var arguments = item["arguments"]?.GetValue<string>();
                        if (!string.IsNullOrWhiteSpace(arguments))
                        {
                            yield return CreateInputJsonDelta(blockIndex, arguments);
                        }
                    }

                    break;
                }
                case "response.output_text.delta":
                {
                    foreach (var update in StartTextBlockIfNeeded())
                    {
                        yield return update;
                    }

                    yield return new JsonObject
                    {
                        ["type"] = "content_block_delta",
                        ["index"] = _activeTextBlockIndex,
                        ["delta"] = new JsonObject
                        {
                            ["type"] = "text_delta",
                            ["text"] = payload["delta"]?.GetValue<string>() ?? string.Empty
                        }
                    };
                    break;
                }
                case "response.function_call_arguments.delta":
                {
                    var itemId = payload["item_id"]?.GetValue<string>() ?? string.Empty;
                    if (_toolBlocksByItemId.TryGetValue(itemId, out var toolBlock))
                    {
                        yield return CreateInputJsonDelta(
                            toolBlock.Index,
                            payload["delta"]?.GetValue<string>() ?? string.Empty);
                    }

                    break;
                }
                case "response.output_item.done":
                {
                    var item = payload["item"]?.AsObject();
                    var itemType = item?["type"]?.GetValue<string>();
                    if (itemType == "function_call")
                    {
                        var itemId = item?["id"]?.GetValue<string>() ?? string.Empty;
                        if (_toolBlocksByItemId.Remove(itemId, out var toolBlock))
                        {
                            yield return new JsonObject
                            {
                                ["type"] = "content_block_stop",
                                ["index"] = toolBlock.Index
                            };
                        }
                    }
                    else if (itemType == "message")
                    {
                        foreach (var update in CloseActiveTextBlock())
                        {
                            yield return update;
                        }
                    }

                    break;
                }
                case "response.completed":
                case "response.incomplete":
                    _finalResponse = payload["response"]?.AsObject();
                    break;
                case "response.failed":
                    yield return new JsonObject
                    {
                        ["type"] = "error",
                        ["error"] = new JsonObject
                        {
                            ["type"] = "api_error",
                            ["message"] = payload["response"]?["error"]?["message"]?.GetValue<string>() ??
                                          payload["error"]?["message"]?.GetValue<string>() ??
                                          "Codex response failed"
                        }
                    };
                    _stopped = true;
                    break;
            }
        }

        public IEnumerable<JsonNode> Complete()
        {
            if (_stopped)
            {
                yield break;
            }

            foreach (var update in CloseActiveTextBlock())
            {
                yield return update;
            }

            foreach (var toolBlock in _toolBlocksByItemId.Values)
            {
                yield return new JsonObject
                {
                    ["type"] = "content_block_stop",
                    ["index"] = toolBlock.Index
                };
            }

            _toolBlocksByItemId.Clear();

            yield return new JsonObject
            {
                ["type"] = "message_delta",
                ["delta"] = new JsonObject
                {
                    ["stop_reason"] = DetermineStopReason(),
                    ["stop_sequence"] = null
                },
                ["usage"] = new JsonObject
                {
                    ["input_tokens"] = _finalResponse?["usage"]?["input_tokens"]?.GetValue<int?>() ?? 0,
                    ["output_tokens"] = _finalResponse?["usage"]?["output_tokens"]?.GetValue<int?>() ?? 0
                }
            };
            yield return new JsonObject
            {
                ["type"] = "message_stop"
            };
            _stopped = true;
        }

        private string DetermineStopReason()
        {
            var output = _finalResponse?["output"]?.AsArray();
            if (_sawToolUse ||
                output?.Any(
                    item => string.Equals(
                        item?["type"]?.GetValue<string>(),
                        "function_call",
                        StringComparison.Ordinal)) == true)
            {
                return "tool_use";
            }

            var incompleteReason = _finalResponse?["incomplete_details"]?["reason"]?.GetValue<string>();
            return !string.IsNullOrWhiteSpace(incompleteReason) &&
                   incompleteReason.Contains("max_output_tokens", StringComparison.Ordinal)
                ? "max_tokens"
                : "end_turn";
        }

        private IEnumerable<JsonNode> StartTextBlockIfNeeded()
        {
            if (_activeTextBlockIndex is not null)
            {
                yield break;
            }

            _activeTextBlockIndex = _nextContentBlockIndex++;
            yield return new JsonObject
            {
                ["type"] = "content_block_start",
                ["index"] = _activeTextBlockIndex,
                ["content_block"] = new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = string.Empty
                }
            };
        }

        private IEnumerable<JsonNode> CloseActiveTextBlock()
        {
            if (_activeTextBlockIndex is null)
            {
                yield break;
            }

            yield return new JsonObject
            {
                ["type"] = "content_block_stop",
                ["index"] = _activeTextBlockIndex
            };
            _activeTextBlockIndex = null;
        }

        private static JsonObject CreateInputJsonDelta(int blockIndex, string partialJson)
        {
            return new JsonObject
            {
                ["type"] = "content_block_delta",
                ["index"] = blockIndex,
                ["delta"] = new JsonObject
                {
                    ["type"] = "input_json_delta",
                    ["partial_json"] = partialJson
                }
            };
        }

        private sealed record ToolBlockState(int Index, string ToolUseId);
    }
}
