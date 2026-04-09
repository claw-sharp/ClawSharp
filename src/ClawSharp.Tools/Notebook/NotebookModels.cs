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

    [JsonPropertyName("execution_count")]
    public int? ExecutionCount { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, object> Metadata { get; set; } = new();

    [JsonPropertyName("outputs")]
    public List<object> Outputs { get; set; } = new();

    [JsonPropertyName("source")]
    public object Source { get; set; } = string.Empty; // text or array of strings
}

public class NotebookMetadata
{
    [JsonPropertyName("kernelspec")]
    public KernelSpec? KernelSpec { get; set; }

    [JsonPropertyName("language_info")]
    public LanguageInfo? LanguageInfo { get; set; }
}

public class KernelSpec
{
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public class LanguageInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}
