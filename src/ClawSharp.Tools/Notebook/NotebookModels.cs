using System.Text.Json.Serialization;

namespace ClawSharp.Tools.Notebook;

public class NotebookContent
{
    [JsonPropertyName("cells")]
    public List<NotebookCell> Cells { get; set; } = new();

    [JsonPropertyName("metadata")]
    public NotebookMetadata Metadata { get; set; } = new();

    [JsonPropertyName("nbformat")]
    public int NbFormat { get; set; }

    [JsonPropertyName("nbformat_minor")]
    public int NbFormatMinor { get; set; }
}

public class NotebookCell
{
    [JsonPropertyName("cell_type")]
    public string CellType { get; set; } = "code";

    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; set; }

    [JsonPropertyName("source")]
    [JsonConverter(typeof(NotebookSourceConverter))]
    public string Source { get; set; } = "";

    [JsonPropertyName("metadata")]
    public Dictionary<string, object> Metadata { get; set; } = new();

    [JsonPropertyName("execution_count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ExecutionCount { get; set; }

    [JsonPropertyName("outputs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<object>? Outputs { get; set; }
}

public class NotebookMetadata
{
    [JsonPropertyName("language_info")]
    public LanguageInfo? LanguageInfo { get; set; }
}

public class LanguageInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "python";
}

public class NotebookSourceConverter : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString();
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var list = new List<string>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                list.Add(reader.GetString() ?? "");
            }
            return string.Join("", list);
        }

        return "";
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        // IPYNB usually stores source as an array of lines for better git diffs, 
        // but it ALSO supports a single string.
        // The source code uses jsonStringify which might default to whatever it was.
        // Let's stick to string for simplicity to match the port.
        writer.WriteStringValue(value);
    }
}
