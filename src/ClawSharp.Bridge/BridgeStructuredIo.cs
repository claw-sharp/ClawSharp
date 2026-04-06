// TS origin: ./cli/structuredIO.ts
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace ClawSharp.Bridge;

public sealed record BridgeStructuredIoDependencies(
    Action<string>? OnDebug = null,
    Action<string, string>? SetEnvironmentVariable = null);

public sealed class BridgeStructuredIo
{
    private const int MaxResolvedToolUseIds = 1000;

    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly bool _replayUserMessages;
    private readonly Action<string>? _onDebug;
    private readonly Action<string, string> _setEnvironmentVariable;
    private readonly Dictionary<string, JsonObject> _pendingRequests = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _resolvedToolUseIds = [];
    private readonly HashSet<string> _resolvedToolUseIdSet = new(StringComparer.Ordinal);
    private readonly Queue<string> _prependedLines = new();
    private Action<JsonObject>? _onControlRequestSent;
    private Action<string>? _onControlRequestResolved;

    public BridgeStructuredIo(
        TextReader input,
        TextWriter output,
        bool replayUserMessages,
        BridgeStructuredIoDependencies? dependencies = null)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _replayUserMessages = replayUserMessages;
        _onDebug = dependencies?.OnDebug;
        _setEnvironmentVariable = dependencies?.SetEnvironmentVariable ?? DefaultSetEnvironmentVariable;
    }

    public IReadOnlyList<JsonObject> GetPendingPermissionRequests()
    {
        return _pendingRequests.Values
            .Where(static request =>
                string.Equals(request["request"]?["subtype"]?.GetValue<string>(), "can_use_tool", StringComparison.Ordinal))
            .Select(static request => request.DeepClone()!.AsObject())
            .ToArray();
    }

    public void RegisterPendingRequest(JsonObject request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestId = request["request_id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(requestId))
        {
            throw new InvalidOperationException("control_request is missing request_id.");
        }

        _pendingRequests[requestId] = request.DeepClone()!.AsObject();
    }

    public void PrependUserMessage(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var message = new JsonObject
        {
            ["type"] = "user",
            ["session_id"] = string.Empty,
            ["message"] = new JsonObject
            {
                ["role"] = "user",
                ["content"] = content
            },
            ["parent_tool_use_id"] = null
        };

        _prependedLines.Enqueue(message.ToJsonString());
    }

    public void SetOnControlRequestSent(Action<JsonObject>? callback)
    {
        _onControlRequestSent = callback;
    }

    public void SetOnControlRequestResolved(Action<string>? callback)
    {
        _onControlRequestResolved = callback;
    }

    public async IAsyncEnumerable<JsonObject> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await ReadNextLineAsync(cancellationToken);
            if (line is null)
            {
                yield break;
            }

            var message = ProcessLine(line);
            if (message is not null)
            {
                yield return message;
            }
        }
    }

    public async Task WriteAsync(JsonObject message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var payload = message.ToJsonString();
        await _output.WriteLineAsync(payload.AsMemory(), cancellationToken);
        await _output.FlushAsync(cancellationToken);

        if (string.Equals(message["type"]?.GetValue<string>(), "control_request", StringComparison.Ordinal) &&
            string.Equals(message["request"]?["subtype"]?.GetValue<string>(), "can_use_tool", StringComparison.Ordinal))
        {
            _onControlRequestSent?.Invoke(message.DeepClone()!.AsObject());
        }
    }

    public async Task<bool> InjectControlResponseAsync(
        JsonObject response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);

        var requestId = response["response"]?["request_id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(requestId) || !_pendingRequests.TryGetValue(requestId, out var request))
        {
            return false;
        }

        TrackResolvedToolUseId(request);
        _pendingRequests.Remove(requestId);
        await WriteAsync(
            new JsonObject
            {
                ["type"] = "control_cancel_request",
                ["request_id"] = requestId
            },
            cancellationToken);
        return true;
    }

    private async Task<string?> ReadNextLineAsync(CancellationToken cancellationToken)
    {
        if (_prependedLines.Count > 0)
        {
            return _prependedLines.Dequeue();
        }

        return await _input.ReadLineAsync(cancellationToken);
    }

    private JsonObject? ProcessLine(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return null;
        }

        JsonNode parsed;
        try
        {
            parsed = BridgeMessagingUtilities.NormalizeControlMessageKeys(JsonNode.Parse(line)) ??
                     throw new InvalidOperationException($"Error parsing streaming input line: {line}");
        }
        catch (Exception error)
        {
            throw new InvalidOperationException($"Error parsing streaming input line: {line}: {error.Message}", error);
        }

        if (parsed is not JsonObject message)
        {
            return null;
        }

        var type = message["type"]?.GetValue<string>();
        switch (type)
        {
            case "keep_alive":
                return null;

            case "update_environment_variables":
                ApplyEnvironmentUpdates(message);
                return null;

            case "control_response":
                return HandleControlResponse(message);

            case "control_request":
                if (message["request"] is null)
                {
                    throw new InvalidOperationException("Error: Missing request on control_request");
                }

                return message;

            case "control_cancel_request":
                if (string.IsNullOrWhiteSpace(message["request_id"]?.GetValue<string>()))
                {
                    throw new InvalidOperationException("Error: Missing request_id on control_cancel_request");
                }

                return message;

            case "result":
                return message;

            case "assistant":
            case "system":
                return message;

            case "user":
                var role = message["message"]?["role"]?.GetValue<string>();
                if (!string.Equals(role, "user", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Error: Expected message role 'user', got '{role}'");
                }

                return message;

            default:
                _onDebug?.Invoke($"[structuredIO] Ignoring unknown message type: {type}");
                return null;
        }
    }

    private void ApplyEnvironmentUpdates(JsonObject message)
    {
        if (message["variables"] is not JsonObject variables)
        {
            return;
        }

        List<string> keys = [];
        foreach (var pair in variables)
        {
            if (pair.Value is null)
            {
                continue;
            }

            var value = pair.Value.GetValue<string>();
            _setEnvironmentVariable(pair.Key, value);
            keys.Add(pair.Key);
        }

        if (keys.Count > 0)
        {
            _onDebug?.Invoke($"[structuredIO] applied update_environment_variables: {string.Join(", ", keys)}");
        }
    }

    private JsonObject? HandleControlResponse(JsonObject message)
    {
        var requestId = message["response"]?["request_id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return _replayUserMessages ? message : null;
        }

        if (!_pendingRequests.TryGetValue(requestId, out var request))
        {
            var toolUseId = message["response"]?["response"]?["toolUseID"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(toolUseId) && _resolvedToolUseIdSet.Contains(toolUseId))
            {
                _onDebug?.Invoke(
                    $"[structuredIO] Ignoring duplicate control_response for already-resolved toolUseID={toolUseId} request_id={requestId}");
            }

            return _replayUserMessages ? message : null;
        }

        TrackResolvedToolUseId(request);
        _pendingRequests.Remove(requestId);

        if (string.Equals(request["request"]?["subtype"]?.GetValue<string>(), "can_use_tool", StringComparison.Ordinal))
        {
            _onControlRequestResolved?.Invoke(requestId);
        }

        return _replayUserMessages ? message : null;
    }

    private void TrackResolvedToolUseId(JsonObject request)
    {
        if (!string.Equals(request["request"]?["subtype"]?.GetValue<string>(), "can_use_tool", StringComparison.Ordinal))
        {
            return;
        }

        var toolUseId = request["request"]?["tool_use_id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(toolUseId) || _resolvedToolUseIdSet.Contains(toolUseId))
        {
            return;
        }

        _resolvedToolUseIdSet.Add(toolUseId);
        _resolvedToolUseIds.AddLast(toolUseId);
        if (_resolvedToolUseIds.Count <= MaxResolvedToolUseIds)
        {
            return;
        }

        var first = _resolvedToolUseIds.First;
        if (first is not null)
        {
            _resolvedToolUseIds.RemoveFirst();
            _resolvedToolUseIdSet.Remove(first.Value);
        }
    }

    private static void DefaultSetEnvironmentVariable(string key, string value)
    {
        Environment.SetEnvironmentVariable(key, value);
    }
}
