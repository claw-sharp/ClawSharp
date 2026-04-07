using System.Text.Json;

namespace ClawSharp.Tools.Notebook;

internal static class NotebookToolInputParser
{
    public static bool TryParseEdit(
        string arguments,
        out NotebookEditToolInputInternal? input,
        out string? errorMessage)
    {
        input = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(arguments))
        {
            errorMessage = "NotebookEdit requires input.";
            return false;
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var dto = JsonSerializer.Deserialize<NotebookEditToolInputDto>(arguments, options);
            if (dto == null || string.IsNullOrWhiteSpace(dto.NotebookPath))
            {
                errorMessage = "NotebookEdit requires 'notebook_path'.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(dto.NewSource) && dto.EditMode != "delete")
            {
                errorMessage = "NotebookEdit requires 'new_source' unless edit_mode is 'delete'.";
                return false;
            }

            input = new NotebookEditToolInputInternal(
                dto.NotebookPath.Trim(),
                dto.CellId,
                dto.NewSource ?? "",
                dto.CellType,
                dto.EditMode ?? "replace"
            );
            return true;
        }
        catch (JsonException)
        {
            errorMessage = "NotebookEdit requires valid JSON input.";
            return false;
        }
    }

    private class NotebookEditToolInputDto
    {
        public string? NotebookPath { get; set; }
        public string? CellId { get; set; }
        public string? NewSource { get; set; }
        public string? CellType { get; set; }
        public string? EditMode { get; set; }
    }
}

internal sealed record NotebookEditToolInputInternal(
    string NotebookPath,
    string? CellId,
    string NewSource,
    string? CellType,
    string EditMode);
