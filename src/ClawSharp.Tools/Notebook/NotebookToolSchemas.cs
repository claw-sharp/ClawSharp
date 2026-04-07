using System.Text.Json.Nodes;

namespace ClawSharp.Tools.Notebook;

internal static class NotebookToolSchemas
{
    public static JsonObject EditInputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("notebook_path", ToolJsonSchemaFactory.String("The absolute path to the Jupyter notebook file to edit (must be absolute, not relative)")),
                ("cell_id", ToolJsonSchemaFactory.String("The ID of the cell to edit. When inserting a new cell, the new cell will be inserted after the cell with this ID, or at the beginning if not specified.")),
                ("new_source", ToolJsonSchemaFactory.String("The new source for the cell")),
                ("cell_type", ToolJsonSchemaFactory.StringEnum(["code", "markdown"], "The type of the cell (code or markdown). If not specified, it defaults to the current cell type. If using edit_mode=insert, this is required.")),
                ("edit_mode", ToolJsonSchemaFactory.StringEnum(["replace", "insert", "delete"], "The type of edit to make (replace, insert, delete). Defaults to replace."))
            ],
            required:
            [
                "notebook_path",
                "new_source"
            ]);

    public static JsonObject EditOutputSchema =>
        ToolJsonSchemaFactory.StrictObject(
            [
                ("new_source", ToolJsonSchemaFactory.String("The new source code that was written to the cell")),
                ("cell_id", ToolJsonSchemaFactory.String("The ID of the cell that was edited")),
                ("cell_type", ToolJsonSchemaFactory.String("The type of the cell")),
                ("language", ToolJsonSchemaFactory.String("The programming language of the notebook")),
                ("edit_mode", ToolJsonSchemaFactory.String("The edit mode that was used")),
                ("error", ToolJsonSchemaFactory.String("Error message if the operation failed")),
                ("notebook_path", ToolJsonSchemaFactory.String("The path to the notebook file")),
                ("original_file", ToolJsonSchemaFactory.String("The original notebook content before modification")),
                ("updated_file", ToolJsonSchemaFactory.String("The updated notebook content after modification"))
            ],
            required:
            [
                "new_source",
                "cell_type",
                "language",
                "edit_mode",
                "notebook_path",
                "original_file",
                "updated_file"
            ]);
}
