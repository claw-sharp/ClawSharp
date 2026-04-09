using System.ComponentModel;
using System.Text.Json.Serialization;

namespace ClawSharp.Tools.Notebook;

public class NotebookEditToolInput
{
    [JsonPropertyName("notebook_path")]
    [Description("The absolute path to the Jupyter notebook file to edit (must be absolute, not relative)")]
    public required string NotebookPath { get; set; }

    [JsonPropertyName("cell_id")]
    [Description("The ID of the cell to edit. When inserting a new cell, the new cell will be inserted after the cell with this ID, or at the beginning if not specified.")]
    public string? CellId { get; set; }

    [JsonPropertyName("new_source")]
    [Description("The new source for the cell")]
    public required string NewSource { get; set; }

    [JsonPropertyName("cell_type")]
    [Description("The type of the cell (code or markdown). If not specified, it defaults to the current cell type. If using edit_mode=insert, this is required.")]
    public string? CellType { get; set; }

    [JsonPropertyName("edit_mode")]
    [Description("The type of edit to make (replace, insert, delete). Defaults to replace.")]
    public string? EditMode { get; set; }
}

public class NotebookEditToolOutput
{
    [JsonPropertyName("new_source")]
    [Description("The new source code that was written to the cell")]
    public required string NewSource { get; set; }

    [JsonPropertyName("cell_id")]
    [Description("The ID of the cell that was edited")]
    public string? CellId { get; set; }

    [JsonPropertyName("cell_type")]
    [Description("The type of the cell")]
    public required string CellType { get; set; }

    [JsonPropertyName("language")]
    [Description("The programming language of the notebook")]
    public required string Language { get; set; }

    [JsonPropertyName("edit_mode")]
    [Description("The edit mode that was used")]
    public required string EditMode { get; set; }

    [JsonPropertyName("error")]
    [Description("Error message if the operation failed")]
    public string? Error { get; set; }

    [JsonPropertyName("notebook_path")]
    [Description("The path to the notebook file")]
    public required string NotebookPath { get; set; }

    [JsonPropertyName("original_file")]
    [Description("The original notebook content before modification")]
    public required string OriginalFile { get; set; }

    [JsonPropertyName("updated_file")]
    [Description("The updated notebook content after modification")]
    public required string UpdatedFile { get; set; }
}
