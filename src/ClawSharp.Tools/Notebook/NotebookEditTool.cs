using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClawSharp.Tools.Notebook;

internal sealed class NotebookEditTool : BaseTool
{
    public NotebookEditTool() : base(new ToolDescriptor(
        Name: "NotebookEdit",
        Description: "Edit a Jupyter notebook (.ipynb) by replacing, inserting, or deleting cells. Always read the notebook first to get the cell IDs and current content.",
        InputSchema: new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["notebook_path"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "The absolute path to the Jupyter notebook file to edit (must be absolute, not relative)"
                },
                ["cell_id"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "The ID of the cell to edit. When inserting a new cell, the new cell will be inserted after the cell with this ID, or at the beginning if not specified."
                },
                ["new_source"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "The new source for the cell"
                },
                ["cell_type"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "The type of the cell (code or markdown). If not specified, it defaults to the current cell type. If using edit_mode=insert, this is required."
                },
                ["edit_mode"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray { "replace", "insert", "delete" },
                    ["description"] = "The type of edit to make (replace, insert, delete). Defaults to replace."
                }
            },
            ["required"] = new JsonArray { "notebook_path", "new_source" }
        }))
    {
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var inputJson = context.Arguments;
        var input = JsonSerializer.Deserialize<NotebookEditInput>(inputJson);
        if (input == null) return Failure("Invalid input");

        var editMode = input.edit_mode ?? "replace";
        var fullPath = Path.IsPathRooted(input.notebook_path) 
            ? input.notebook_path 
            : Path.GetFullPath(Path.Combine(context.WorkspaceRoot, input.notebook_path));

        if (Path.GetExtension(fullPath) != ".ipynb")
        {
            return Failure("File must be a Jupyter notebook (.ipynb file).");
        }

        if (editMode == "insert" && string.IsNullOrEmpty(input.cell_type))
        {
            return Failure("Cell type is required when using edit_mode=insert.");
        }

        try
        {
            var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var notebook = JsonSerializer.Deserialize<NotebookContent>(content);
            if (notebook == null) return Failure("Notebook is not valid JSON.");

            int cellIndex = -1;
            if (string.IsNullOrEmpty(input.cell_id))
            {
                cellIndex = 0;
            }
            else
            {
                // Try to find by ID
                cellIndex = notebook.Cells.FindIndex(c => c.Id == input.cell_id);

                if (cellIndex == -1)
                {
                    // Try to parse cell-N
                    if (input.cell_id.StartsWith("cell-") && int.TryParse(input.cell_id.AsSpan(5), out int parsedIndex))
                    {
                        if (parsedIndex >= 0 && parsedIndex < notebook.Cells.Count)
                        {
                            cellIndex = parsedIndex;
                        }
                    }
                }

                if (cellIndex == -1)
                {
                    return Failure($"Cell with ID \"{input.cell_id}\" not found in notebook.");
                }

                if (editMode == "insert")
                {
                    cellIndex++; // Insert after
                }
            }

            if (editMode == "replace" && cellIndex == notebook.Cells.Count)
            {
                editMode = "insert";
                input.cell_type ??= "code";
            }

            string? newCellId = null;
            if (notebook.NbFormat > 4 || (notebook.NbFormat == 4 && notebook.NbFormatMinor >= 5))
            {
                if (editMode == "insert")
                {
                    newCellId = Guid.NewGuid().ToString("n").Substring(0, 12);
                }
                else
                {
                    newCellId = input.cell_id;
                }
            }

            if (editMode == "delete")
            {
                notebook.Cells.RemoveAt(cellIndex);
            }
            else if (editMode == "insert")
            {
                var newCell = new NotebookCell
                {
                    CellType = input.cell_type ?? "code",
                    Id = newCellId,
                    Source = input.new_source,
                    Metadata = new()
                };
                notebook.Cells.Insert(cellIndex, newCell);
            }
            else
            {
                var targetCell = notebook.Cells[cellIndex];
                targetCell.Source = input.new_source;
                if (targetCell.CellType == "code")
                {
                    targetCell.ExecutionCount = null;
                    targetCell.Outputs = new();
                }
                if (!string.IsNullOrEmpty(input.cell_type))
                {
                    targetCell.CellType = input.cell_type;
                }
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var updatedContent = JsonSerializer.Serialize(notebook, options);
            await File.WriteAllTextAsync(fullPath, updatedContent, cancellationToken);

            var result = new JsonObject
            {
                ["new_source"] = input.new_source,
                ["cell_id"] = newCellId ?? input.cell_id,
                ["cell_type"] = input.cell_type ?? "code",
                ["language"] = notebook.Metadata.LanguageInfo?.Name ?? "python",
                ["edit_mode"] = editMode,
                ["notebook_path"] = fullPath
            };

            return Success("Successfully edited notebook cell.", result);
        }
        catch (Exception ex)
        {
            return Failure(ex.Message);
        }
    }

    private class NotebookEditInput
    {
        public string notebook_path { get; set; } = string.Empty;
        public string? cell_id { get; set; }
        public string new_source { get; set; } = string.Empty;
        public string? cell_type { get; set; }
        public string? edit_mode { get; set; }
    }
}
