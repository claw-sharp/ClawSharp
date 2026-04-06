// TS origin: ./Tool.ts
using System.Text.Json.Nodes;

namespace ClawSharp.Tools;

internal static class ToolJsonSchemaFactory
{
    public static JsonObject StrictObject(
        IEnumerable<(string Name, JsonNode Schema)> properties,
        IEnumerable<string>? required = null,
        string? description = null)
    {
        var propertyObject = new JsonObject();
        foreach (var (name, schema) in properties)
        {
            propertyObject[name] = schema.DeepClone();
        }

        var result = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = propertyObject,
            ["additionalProperties"] = false
        };

        if (!string.IsNullOrWhiteSpace(description))
        {
            result["description"] = description;
        }

        if (required is not null)
        {
            var requiredArray = new JsonArray();
            foreach (var name in required)
            {
                requiredArray.Add(name);
            }

            if (requiredArray.Count > 0)
            {
                result["required"] = requiredArray;
            }
        }

        return result;
    }

    public static JsonObject String(string? description = null)
    {
        return Primitive("string", description);
    }

    public static JsonObject Number(
        string? description = null,
        double? minimum = null,
        double? maximum = null)
    {
        var result = Primitive("number", description);
        if (minimum is not null)
        {
            result["minimum"] = minimum.Value;
        }

        if (maximum is not null)
        {
            result["maximum"] = maximum.Value;
        }

        return result;
    }

    public static JsonObject Integer(
        string? description = null,
        int? minimum = null,
        int? maximum = null)
    {
        var result = Primitive("integer", description);
        if (minimum is not null)
        {
            result["minimum"] = minimum.Value;
        }

        if (maximum is not null)
        {
            result["maximum"] = maximum.Value;
        }

        return result;
    }

    public static JsonObject Boolean(string? description = null, bool? defaultValue = null)
    {
        var result = Primitive("boolean", description);
        if (defaultValue is not null)
        {
            result["default"] = defaultValue.Value;
        }

        return result;
    }

    public static JsonObject Array(JsonNode items, string? description = null)
    {
        var result = new JsonObject
        {
            ["type"] = "array",
            ["items"] = items.DeepClone()
        };

        if (!string.IsNullOrWhiteSpace(description))
        {
            result["description"] = description;
        }

        return result;
    }

    public static JsonObject StringEnum(IEnumerable<string> values, string? description = null)
    {
        var result = Primitive("string", description);
        var enumArray = new JsonArray();
        foreach (var value in values)
        {
            enumArray.Add(value);
        }

        result["enum"] = enumArray;
        return result;
    }

    public static JsonObject Nullable(JsonNode schema, string? description = null)
    {
        var alternatives = new JsonArray
        {
            schema.DeepClone(),
            new JsonObject
            {
                ["type"] = "null"
            }
        };

        var result = new JsonObject
        {
            ["anyOf"] = alternatives
        };

        if (!string.IsNullOrWhiteSpace(description))
        {
            result["description"] = description;
        }

        return result;
    }

    public static JsonObject OneOf(params JsonNode[] schemas)
    {
        var alternatives = new JsonArray();
        foreach (var schema in schemas)
        {
            alternatives.Add(schema.DeepClone());
        }

        return new JsonObject
        {
            ["oneOf"] = alternatives
        };
    }

    private static JsonObject Primitive(string type, string? description)
    {
        var result = new JsonObject
        {
            ["type"] = type
        };

        if (!string.IsNullOrWhiteSpace(description))
        {
            result["description"] = description;
        }

        return result;
    }
}
