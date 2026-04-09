using System.Text.Json.Nodes;

namespace ClawSharp.Query;

internal static class OpenAiSchemaSanitizer
{
    private static readonly HashSet<string> IncompatibleKeywords =
    [
        "$comment",
        "$schema",
        "default",
        "else",
        "examples",
        "format",
        "if",
        "maxLength",
        "maximum",
        "minLength",
        "minimum",
        "multipleOf",
        "pattern",
        "patternProperties",
        "propertyNames",
        "then",
        "unevaluatedProperties"
    ];

    private static readonly HashSet<string> AllowedTypes =
    [
        "string",
        "number",
        "integer",
        "boolean",
        "object",
        "array",
        "null"
    ];

    public static JsonObject SanitizeForOpenAiCompat(JsonNode? schema)
    {
        var stripped = StripIncompatibleKeywords(schema);
        if (stripped is not JsonObject record)
        {
            return [];
        }

        var result = (JsonObject)record.DeepClone();
        SanitizeTypeField(result);
        SanitizeProperties(result);
        SanitizeItems(result);
        SanitizeCombinators(result);
        SanitizeRequired(result);
        SanitizeEnum(result);
        SanitizeConst(result);
        return result;
    }

    public static JsonObject EnforceCodexStrictSchema(JsonNode? schema)
    {
        var record = SanitizeForOpenAiCompat(schema);

        if (string.Equals(record["type"]?.GetValue<string>(), "object", StringComparison.Ordinal))
        {
            record["additionalProperties"] = false;

            if (record["properties"] is JsonObject properties)
            {
                var enforcedProperties = new JsonObject();
                foreach (var pair in properties.ToList())
                {
                    var strictValue = EnforceCodexStrictSchema(pair.Value);
                    if (IsEmptyStrictObject(strictValue))
                    {
                        continue;
                    }

                    enforcedProperties[pair.Key] = strictValue;
                }

                record["properties"] = enforcedProperties;
                record["required"] = new JsonArray(
                    enforcedProperties
                        .Select(static pair => JsonValue.Create(pair.Key))
                        .ToArray());
            }
            else
            {
                record["required"] = new JsonArray();
            }
        }

        if (record["items"] is JsonArray itemArray)
        {
            var sanitizedItems = new JsonArray();
            foreach (var item in itemArray)
            {
                sanitizedItems.Add(EnforceCodexStrictSchema(item));
            }

            record["items"] = sanitizedItems;
        }
        else if (record["items"] is not null)
        {
            record["items"] = EnforceCodexStrictSchema(record["items"]);
        }

        foreach (var key in new[] { "anyOf", "oneOf", "allOf" })
        {
            if (record[key] is not JsonArray alternatives)
            {
                continue;
            }

            var sanitizedAlternatives = new JsonArray();
            foreach (var alternative in alternatives)
            {
                sanitizedAlternatives.Add(EnforceCodexStrictSchema(alternative));
            }

            record[key] = sanitizedAlternatives;
        }

        return record;
    }

    public static JsonObject EnforceOpenAiStrictSchema(JsonNode? schema)
    {
        var record = SanitizeForOpenAiCompat(schema);
        EnforceOpenAiStrictSchemaCore(record);
        return record;
    }

    private static JsonNode? StripIncompatibleKeywords(JsonNode? node)
    {
        return node switch
        {
            JsonObject obj => StripObject(obj),
            JsonArray array => new JsonArray(array.Select(StripIncompatibleKeywords).ToArray()),
            null => null,
            _ => node.DeepClone()
        };
    }

    private static JsonObject StripObject(JsonObject obj)
    {
        var result = new JsonObject();
        foreach (var pair in obj)
        {
            if (IncompatibleKeywords.Contains(pair.Key))
            {
                continue;
            }

            result[pair.Key] = StripIncompatibleKeywords(pair.Value);
        }

        return result;
    }

    private static void SanitizeTypeField(JsonObject record)
    {
        switch (record["type"])
        {
            case JsonValue value when value.TryGetValue<string>(out var type):
                if (!AllowedTypes.Contains(type))
                {
                    record.Remove("type");
                }

                break;
            case JsonArray types:
            {
                var filteredTypes = types
                    .Select(static node => node?.GetValue<string>())
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Cast<string>()
                    .Where(AllowedTypes.Contains)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                record["type"] = filteredTypes.Length switch
                {
                    0 => null,
                    1 => filteredTypes[0],
                    _ => new JsonArray(filteredTypes.Select(static value => JsonValue.Create(value)).ToArray())
                };
                if (filteredTypes.Length == 0)
                {
                    record.Remove("type");
                }

                break;
            }
        }
    }

    private static void SanitizeProperties(JsonObject record)
    {
        if (record["properties"] is not JsonObject properties)
        {
            return;
        }

        var sanitizedProperties = new JsonObject();
        foreach (var pair in properties)
        {
            sanitizedProperties[pair.Key] = SanitizeForOpenAiCompat(pair.Value);
        }

        record["properties"] = sanitizedProperties;
    }

    private static void SanitizeItems(JsonObject record)
    {
        if (!record.TryGetPropertyValue("items", out var items))
        {
            return;
        }

        record["items"] = items switch
        {
            JsonArray itemArray => new JsonArray(itemArray.Select(SanitizeForOpenAiCompat).Cast<JsonNode?>().ToArray()),
            _ => SanitizeForOpenAiCompat(items)
        };
    }

    private static void SanitizeCombinators(JsonObject record)
    {
        foreach (var key in new[] { "anyOf", "oneOf", "allOf" })
        {
            if (record[key] is not JsonArray alternatives)
            {
                continue;
            }

            record[key] = new JsonArray(alternatives.Select(SanitizeForOpenAiCompat).Cast<JsonNode?>().ToArray());
        }
    }

    private static void SanitizeRequired(JsonObject record)
    {
        if (record["required"] is not JsonArray required ||
            record["properties"] is not JsonObject properties)
        {
            return;
        }

        record["required"] = new JsonArray(
            required
                .Select(static item => item?.GetValue<string>())
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .Where(properties.ContainsKey)
                .Distinct(StringComparer.Ordinal)
                .Select(static item => JsonValue.Create(item))
                .ToArray());
    }

    private static void SanitizeEnum(JsonObject record)
    {
        if (record["enum"] is not JsonArray values)
        {
            return;
        }

        var schemaWithoutEnum = (JsonObject)record.DeepClone();
        schemaWithoutEnum.Remove("enum");
        var filteredValues = values
            .Where(value => SchemaAllowsValue(schemaWithoutEnum, value))
            .Select(static value => value?.DeepClone())
            .ToArray();

        if (filteredValues.Length == 0)
        {
            record.Remove("enum");
            return;
        }

        record["enum"] = new JsonArray(filteredValues);
    }

    private static void SanitizeConst(JsonObject record)
    {
        if (!record.TryGetPropertyValue("const", out var value))
        {
            return;
        }

        var schemaWithoutConst = (JsonObject)record.DeepClone();
        schemaWithoutConst.Remove("const");
        if (!SchemaAllowsValue(schemaWithoutConst, value))
        {
            record.Remove("const");
        }
    }

    private static bool SchemaAllowsValue(JsonObject schema, JsonNode? value)
    {
        if (schema["anyOf"] is JsonArray anyOf)
        {
            return anyOf.Any(item => SchemaAllowsValue(SanitizeForOpenAiCompat(item), value));
        }

        if (schema["oneOf"] is JsonArray oneOf)
        {
            return oneOf.Count(item => SchemaAllowsValue(SanitizeForOpenAiCompat(item), value)) == 1;
        }

        if (schema["allOf"] is JsonArray allOf)
        {
            return allOf.All(item => SchemaAllowsValue(SanitizeForOpenAiCompat(item), value));
        }

        if (schema.TryGetPropertyValue("const", out var constValue) &&
            !DeepEquals(constValue, value))
        {
            return false;
        }

        if (schema["enum"] is JsonArray enumValues &&
            !enumValues.Any(item => DeepEquals(item, value)))
        {
            return false;
        }

        var types = GetJsonSchemaTypes(schema);
        if (types.Count > 0 && !types.Any(type => MatchesJsonSchemaType(type, value)))
        {
            return false;
        }

        return true;
    }

    private static IReadOnlyList<string> GetJsonSchemaTypes(JsonObject schema)
    {
        return schema["type"] switch
        {
            JsonValue value when value.TryGetValue<string>(out var type) => [type],
            JsonArray values => values
                .Select(static item => item?.GetValue<string>())
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToArray(),
            _ => []
        };
    }

    private static bool MatchesJsonSchemaType(string type, JsonNode? value)
    {
        return type switch
        {
            "string" => value is JsonValue stringValue && stringValue.TryGetValue<string>(out _),
            "number" => value is JsonValue numberValue && numberValue.TryGetValue<double>(out _),
            "integer" => value is JsonValue integerValue && integerValue.TryGetValue<int>(out _),
            "boolean" => value is JsonValue booleanValue && booleanValue.TryGetValue<bool>(out _),
            "object" => value is JsonObject,
            "array" => value is JsonArray,
            "null" => value is null,
            _ => true
        };
    }

    private static bool DeepEquals(JsonNode? left, JsonNode? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left.GetType() != right.GetType())
        {
            return false;
        }

        return left switch
        {
            JsonValue leftValue => right is JsonValue rightValue &&
                                   string.Equals(leftValue.ToJsonString(), rightValue.ToJsonString(), StringComparison.Ordinal),
            JsonArray leftArray => right is JsonArray rightArray &&
                                   leftArray.Count == rightArray.Count &&
                                   leftArray.Zip(rightArray, DeepEquals).All(static equal => equal),
            JsonObject leftObject => right is JsonObject rightObject &&
                                     leftObject.Count == rightObject.Count &&
                                     leftObject.All(pair =>
                                         rightObject.TryGetPropertyValue(pair.Key, out var rightValue) &&
                                         DeepEquals(pair.Value, rightValue)),
            _ => string.Equals(left.ToJsonString(), right.ToJsonString(), StringComparison.Ordinal)
        };
    }

    private static bool IsEmptyStrictObject(JsonObject schema)
    {
        return string.Equals(schema["type"]?.GetValue<string>(), "object", StringComparison.Ordinal) &&
               schema["additionalProperties"]?.GetValue<bool?>() == false &&
               schema["properties"] is JsonObject properties &&
               properties.Count == 0;
    }

    private static void EnforceOpenAiStrictSchemaCore(JsonObject record)
    {
        if (string.Equals(record["type"]?.GetValue<string>(), "object", StringComparison.Ordinal))
        {
            var originalRequired = record["required"] is JsonArray requiredArray
                ? new HashSet<string>(
                    requiredArray
                        .Select(static item => item?.GetValue<string>())
                        .Where(static item => !string.IsNullOrWhiteSpace(item))
                        .Cast<string>(),
                    StringComparer.Ordinal)
                : [];

            if (record["properties"] is JsonObject properties)
            {
                var enforcedProperties = new JsonObject();
                foreach (var pair in properties.ToList())
                {
                    if (pair.Value is null)
                    {
                        continue;
                    }

                    var propertySchema = SanitizeForOpenAiCompat(pair.Value);
                    EnforceOpenAiStrictSchemaCore(propertySchema);

                    if (!originalRequired.Contains(pair.Key))
                    {
                        propertySchema = EnsureNullable(propertySchema);
                    }

                    enforcedProperties[pair.Key] = propertySchema;
                }

                record["properties"] = enforcedProperties;
                record["required"] = new JsonArray(
                    enforcedProperties
                        .Select(static pair => JsonValue.Create(pair.Key))
                        .ToArray());
            }
            else
            {
                record["required"] = new JsonArray();
            }
        }

        if (record["items"] is JsonArray itemArray)
        {
            var sanitizedItems = new JsonArray();
            foreach (var item in itemArray)
            {
                if (SanitizeForOpenAiCompat(item) is JsonObject itemSchema)
                {
                    EnforceOpenAiStrictSchemaCore(itemSchema);
                    sanitizedItems.Add(itemSchema);
                }
                else
                {
                    sanitizedItems.Add(item?.DeepClone());
                }
            }

            record["items"] = sanitizedItems;
        }
        else if (record["items"] is JsonObject itemSchema)
        {
            EnforceOpenAiStrictSchemaCore(itemSchema);
            record["items"] = itemSchema;
        }

        foreach (var key in new[] { "anyOf", "oneOf", "allOf" })
        {
            if (record[key] is not JsonArray alternatives)
            {
                continue;
            }

            var sanitizedAlternatives = new JsonArray();
            foreach (var alternative in alternatives)
            {
                if (SanitizeForOpenAiCompat(alternative) is JsonObject alternativeSchema)
                {
                    EnforceOpenAiStrictSchemaCore(alternativeSchema);
                    sanitizedAlternatives.Add(alternativeSchema);
                }
                else
                {
                    sanitizedAlternatives.Add(alternative?.DeepClone());
                }
            }

            record[key] = sanitizedAlternatives;
        }
    }

    private static JsonObject EnsureNullable(JsonObject schema)
    {
        if (AllowsNull(schema))
        {
            return schema;
        }

        return new JsonObject
        {
            ["anyOf"] = new JsonArray(
                schema.DeepClone(),
                new JsonObject
                {
                    ["type"] = "null"
                })
        };
    }

    private static bool AllowsNull(JsonObject schema)
    {
        var types = GetJsonSchemaTypes(schema);
        if (types.Contains("null", StringComparer.Ordinal))
        {
            return true;
        }

        foreach (var key in new[] { "anyOf", "oneOf", "allOf" })
        {
            if (schema[key] is not JsonArray alternatives)
            {
                continue;
            }

            if (alternatives
                .OfType<JsonObject>()
                .Any(AllowsNull))
            {
                return true;
            }
        }

        return false;
    }
}
