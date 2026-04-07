using System.Text.Json.Nodes;

namespace ClawSharp.Bridge;

public static class SessionRunnerUtilities
{
    public const int MaxActivities = 10;
    public const int MaxStderrLines = 10;

    private static readonly IReadOnlyDictionary<string, string> ToolVerbs = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Read"] = "Reading",
        ["Write"] = "Writing",
        ["Edit"] = "Editing",
        ["MultiEdit"] = "Editing",
        ["Bash"] = "Running",
        ["Glob"] = "Searching",
        ["Grep"] = "Searching",
        ["WebFetch"] = "Fetching",
        ["WebSearch"] = "Searching",
        ["Task"] = "Running task",
        ["FileReadTool"] = "Reading",
        ["FileWriteTool"] = "Writing",
        ["FileEditTool"] = "Editing",
        ["GlobTool"] = "Searching",
        ["GrepTool"] = "Searching",
        ["BashTool"] = "Running",
        ["NotebookEditTool"] = "Editing notebook",
        ["LSP"] = "LSP"
    };

    public static string SafeFilenameId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        return string.Concat(id.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '_' or '-'
                ? character
                : '_'));
    }

    public static string ToolSummary(string name, IReadOnlyDictionary<string, object?> input)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(input);

        var verb = ToolVerbs.TryGetValue(name, out var knownVerb) ? knownVerb : name;
        var target = GetStringValue(input, "file_path") ??
                     GetStringValue(input, "filePath") ??
                     GetStringValue(input, "pattern") ??
                     Truncate(GetStringValue(input, "command"), 60) ??
                     GetStringValue(input, "url") ??
                     GetStringValue(input, "query") ??
                     string.Empty;
        return string.IsNullOrEmpty(target) ? verb : $"{verb} {target}";
    }

    public static IReadOnlyList<SessionActivity> ExtractActivities(
        string line,
        string sessionId,
        Action<string>? onDebug = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(sessionId);

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(line);
        }
        catch
        {
            return [];
        }

        if (parsed is not JsonObject message)
        {
            return [];
        }

        List<SessionActivity> activities = [];
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        switch (message["type"]?.GetValue<string>())
        {
            case "assistant":
            {
                if (message["message"] is not JsonObject assistantMessage ||
                    assistantMessage["content"] is not JsonArray content)
                {
                    break;
                }

                foreach (var blockNode in content)
                {
                    if (blockNode is not JsonObject block)
                    {
                        continue;
                    }

                    switch (block["type"]?.GetValue<string>())
                    {
                        case "tool_use":
                        {
                            var name = block["name"]?.GetValue<string>() ?? "Tool";
                            var inputObject = block["input"] as JsonObject;
                            var input = inputObject is null
                                ? new Dictionary<string, object?>(StringComparer.Ordinal)
                                : ToDictionary(inputObject);
                            var summary = ToolSummary(name, input);
                            activities.Add(new SessionActivity(SessionActivityType.ToolStart, summary, now));
                            onDebug?.Invoke($"[bridge:activity] sessionId={sessionId} tool_use name={name} {InputPreview(input)}");
                            break;
                        }
                        case "text":
                        {
                            var text = block["text"]?.GetValue<string>() ?? string.Empty;
                            if (text.Length == 0)
                            {
                                break;
                            }

                            activities.Add(new SessionActivity(
                                SessionActivityType.Text,
                                Truncate(text, 80) ?? string.Empty,
                                now));
                            onDebug?.Invoke($"[bridge:activity] sessionId={sessionId} text \"{Truncate(text, 100)}\"");
                            break;
                        }
                    }
                }

                break;
            }
            case "result":
            {
                var subtype = message["subtype"]?.GetValue<string>();
                if (subtype == "success")
                {
                    activities.Add(new SessionActivity(SessionActivityType.Result, "Session completed", now));
                    onDebug?.Invoke($"[bridge:activity] sessionId={sessionId} result subtype=success");
                }
                else if (!string.IsNullOrEmpty(subtype))
                {
                    var errors = message["errors"] as JsonArray;
                    var errorSummary = errors?[0]?.GetValue<string>() ?? $"Error: {subtype}";
                    activities.Add(new SessionActivity(SessionActivityType.Error, errorSummary, now));
                    onDebug?.Invoke($"[bridge:activity] sessionId={sessionId} result subtype={subtype} error=\"{errorSummary}\"");
                }
                else
                {
                    onDebug?.Invoke($"[bridge:activity] sessionId={sessionId} result subtype=undefined");
                }

                break;
            }
        }

        return activities;
    }

    public static string? ExtractUserMessageText(IReadOnlyDictionary<string, object?> message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (TryGetValue(message, "parent_tool_use_id") is not null ||
            IsTruthy(TryGetValue(message, "isSynthetic")) ||
            IsTruthy(TryGetValue(message, "isReplay")))
        {
            return null;
        }

        if (TryGetValue(message, "message") is not IReadOnlyDictionary<string, object?> innerMessage)
        {
            return null;
        }

        string? text = null;
        if (TryGetValue(innerMessage, "content") is string stringContent)
        {
            text = stringContent;
        }
        else if (TryGetValue(innerMessage, "content") is IReadOnlyList<object?> blocks)
        {
            foreach (var block in blocks)
            {
                if (block is IReadOnlyDictionary<string, object?> blockObject &&
                    string.Equals(TryGetValue(blockObject, "type") as string, "text", StringComparison.Ordinal))
                {
                    text = TryGetValue(blockObject, "text") as string;
                    break;
                }
            }
        }

        text = text?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    public static string? ExtractUserMessageText(JsonObject message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return ExtractUserMessageText(ToDictionary(message));
    }

    public static string InputPreview(IReadOnlyDictionary<string, object?> input)
    {
        ArgumentNullException.ThrowIfNull(input);

        List<string> parts = [];
        foreach (var pair in input)
        {
            if (pair.Value is string stringValue)
            {
                parts.Add($"{pair.Key}=\"{Truncate(stringValue, 100)}\"");
            }

            if (parts.Count >= 3)
            {
                break;
            }
        }

        return string.Join(' ', parts);
    }

    private static string? GetStringValue(IReadOnlyDictionary<string, object?> input, string key)
    {
        return input.TryGetValue(key, out var value) ? value as string : null;
    }

    private static object? TryGetValue(IReadOnlyDictionary<string, object?> dictionary, string key)
    {
        return dictionary.TryGetValue(key, out var value) ? value : null;
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (value is null)
        {
            return null;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static bool IsTruthy(object? value)
    {
        return value switch
        {
            null => false,
            false => false,
            bool boolean => boolean,
            string text => text.Length > 0,
            _ => true
        };
    }

    private static Dictionary<string, object?> ToDictionary(JsonObject obj)
    {
        Dictionary<string, object?> dictionary = new(StringComparer.Ordinal);
        foreach (var pair in obj)
        {
            dictionary[pair.Key] = pair.Value switch
            {
                JsonValue value when value.TryGetValue<string>(out var stringValue) => stringValue,
                JsonValue value when value.TryGetValue<bool>(out var boolValue) => boolValue,
                JsonValue value when value.TryGetValue<long>(out var longValue) => longValue,
                JsonValue value when value.TryGetValue<double>(out var doubleValue) => doubleValue,
                JsonObject nested => ToDictionary(nested),
                JsonArray array => array.Select(node => node switch
                {
                    JsonValue itemValue when itemValue.TryGetValue<string>(out var arrayString) => (object?)arrayString,
                    JsonObject nestedObject => ToDictionary(nestedObject),
                    _ => node?.ToJsonString()
                }).ToList(),
                _ => pair.Value?.ToJsonString()
            };
        }

        return dictionary;
    }
}
