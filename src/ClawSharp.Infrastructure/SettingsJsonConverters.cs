// TS origin: ./utils/settings/types.ts, ./schemas/hooks.ts
using System.Text.Json;
using System.Text.Json.Serialization;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

internal sealed class PluginEnabledSettingJsonConverter : JsonConverter<PluginEnabledSetting>
{
    public override PluginEnabledSetting Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.True)
        {
            return new PluginEnabledSetting { Enabled = true };
        }

        if (reader.TokenType == JsonTokenType.False)
        {
            return new PluginEnabledSetting { Enabled = false };
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var versions = JsonSerializer.Deserialize<List<string>>(ref reader, options) ?? [];
            return new PluginEnabledSetting { VersionConstraints = versions };
        }

        throw new JsonException("enabledPlugins entries must be boolean or string array.");
    }

    public override void Write(Utf8JsonWriter writer, PluginEnabledSetting value, JsonSerializerOptions options)
    {
        if (value.VersionConstraints is { Count: > 0 })
        {
            JsonSerializer.Serialize(writer, value.VersionConstraints, options);
            return;
        }

        writer.WriteBooleanValue(value.Enabled ?? false);
    }
}

internal sealed class HookCommandDefinitionJsonConverter : JsonConverter<HookCommandDefinition>
{
    public override HookCommandDefinition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        if (!root.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            throw new JsonException("Hook definitions must include a string type.");
        }

        var typeName = typeElement.GetString();
        var type = typeName switch
        {
            "command" => HookKind.Command,
            "prompt" => HookKind.Prompt,
            "agent" => HookKind.Agent,
            "http" => HookKind.Http,
            _ => throw new JsonException($"Unsupported hook type '{typeName}'.")
        };

        return new HookCommandDefinition(
            type,
            Command: TryGetString(root, "command"),
            Prompt: TryGetString(root, "prompt"),
            Url: TryGetString(root, "url"),
            If: TryGetString(root, "if"),
            Shell: TryGetShell(root),
            TimeoutSeconds: TryGetInt32(root, "timeout"),
            StatusMessage: TryGetString(root, "statusMessage"),
            Once: TryGetBoolean(root, "once"),
            Async: TryGetBoolean(root, "async"),
            AsyncRewake: TryGetBoolean(root, "asyncRewake"),
            Headers: TryGetDictionary(root, "headers"),
            AllowedEnvVars: TryGetStringList(root, "allowedEnvVars"),
            Model: TryGetString(root, "model"));
    }

    public override void Write(Utf8JsonWriter writer, HookCommandDefinition value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("type", value.Type switch
        {
            HookKind.Command => "command",
            HookKind.Prompt => "prompt",
            HookKind.Agent => "agent",
            HookKind.Http => "http",
            _ => throw new JsonException($"Unsupported hook type '{value.Type}'.")
        });

        if (!string.IsNullOrWhiteSpace(value.Command))
        {
            writer.WriteString("command", value.Command);
        }

        if (!string.IsNullOrWhiteSpace(value.Prompt))
        {
            writer.WriteString("prompt", value.Prompt);
        }

        if (!string.IsNullOrWhiteSpace(value.Url))
        {
            writer.WriteString("url", value.Url);
        }

        if (!string.IsNullOrWhiteSpace(value.If))
        {
            writer.WriteString("if", value.If);
        }

        if (value.Shell is not null)
        {
            writer.WriteString("shell", value.Shell == HookShell.PowerShell ? "powershell" : "bash");
        }

        if (value.TimeoutSeconds is not null)
        {
            writer.WriteNumber("timeout", value.TimeoutSeconds.Value);
        }

        if (!string.IsNullOrWhiteSpace(value.StatusMessage))
        {
            writer.WriteString("statusMessage", value.StatusMessage);
        }

        if (value.Once)
        {
            writer.WriteBoolean("once", true);
        }

        if (value.Async)
        {
            writer.WriteBoolean("async", true);
        }

        if (value.AsyncRewake)
        {
            writer.WriteBoolean("asyncRewake", true);
        }

        if (value.Headers is not null)
        {
            writer.WritePropertyName("headers");
            JsonSerializer.Serialize(writer, value.Headers, options);
        }

        if (value.AllowedEnvVars is not null)
        {
            writer.WritePropertyName("allowedEnvVars");
            JsonSerializer.Serialize(writer, value.AllowedEnvVars, options);
        }

        if (!string.IsNullOrWhiteSpace(value.Model))
        {
            writer.WriteString("model", value.Model);
        }

        writer.WriteEndObject();
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static int? TryGetInt32(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static bool TryGetBoolean(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True
            ? true
            : false;
    }

    private static HookShell? TryGetShell(JsonElement element)
    {
        var shell = TryGetString(element, "shell");
        return shell switch
        {
            "bash" => HookShell.Bash,
            "powershell" => HookShell.PowerShell,
            _ => null
        };
    }

    private static IReadOnlyDictionary<string, string>? TryGetDictionary(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in property.EnumerateObject())
        {
            if (item.Value.ValueKind == JsonValueKind.String)
            {
                result[item.Name] = item.Value.GetString()!;
            }
        }

        return result;
    }

    private static IReadOnlyList<string>? TryGetStringList(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return property.EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString()!)
            .ToArray();
    }
}
