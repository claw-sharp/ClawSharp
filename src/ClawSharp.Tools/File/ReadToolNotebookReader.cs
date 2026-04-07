using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClawSharp.Tools;

internal static class ReadToolNotebookReader
{
    private const int LargeOutputThreshold = 10000;

    public static NotebookReadResult Read(string filePath, int maxSizeBytes)
    {
        var content = File.ReadAllText(filePath, Encoding.UTF8);
        JsonObject notebook;
        try
        {
            notebook = JsonNode.Parse(content)?.AsObject()
                ?? throw new InvalidOperationException("Notebook file is invalid JSON.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Notebook file is invalid JSON.", exception);
        }

        var language = notebook["metadata"]?["language_info"]?["name"]?.GetValue<string>() ?? "python";
        var cellNodes = notebook["cells"] as JsonArray
            ?? throw new InvalidOperationException("Notebook file is missing a 'cells' array.");

        var cells = new JsonArray();
        for (var index = 0; index < cellNodes.Count; index++)
        {
            if (cellNodes[index] is not JsonObject cell)
            {
                continue;
            }

            cells.Add(ProcessCell(cell, index, language, includeLargeOutputs: false));
        }

        var cellsJson = cells.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false
        });

        var cellsJsonBytes = Encoding.UTF8.GetByteCount(cellsJson);
        if (cellsJsonBytes > maxSizeBytes)
        {
            throw new InvalidOperationException(
                $"Notebook content ({FormatFileSize(cellsJsonBytes)}) exceeds maximum allowed size ({FormatFileSize(maxSizeBytes)}). " +
                "Use Bash with jq to read specific portions.");
        }

        return new NotebookReadResult(cells, cellsJson);
    }

    private static JsonObject ProcessCell(
        JsonObject cell,
        int index,
        string codeLanguage,
        bool includeLargeOutputs)
    {
        var cellType = cell["cell_type"]?.GetValue<string>() ?? "unknown";
        var cellId = cell["id"]?.GetValue<string>() ?? $"cell-{index}";

        var cellData = new JsonObject
        {
            ["cellType"] = cellType,
            ["source"] = FlattenString(cell["source"]),
            ["cell_id"] = cellId
        };

        if (cellType == "code")
        {
            cellData["language"] = codeLanguage;
            if (cell["execution_count"] is not null && cell["execution_count"]?.GetValueKind() != JsonValueKind.Null)
            {
                cellData["execution_count"] = cell["execution_count"]!.DeepClone();
            }
        }

        if (cellType == "code" && cell["outputs"] is JsonArray outputs && outputs.Count > 0)
        {
            var processedOutputs = new List<JsonObject>();
            foreach (var outputNode in outputs)
            {
                if (outputNode is JsonObject output)
                {
                    processedOutputs.Add(ProcessOutput(output));
                }
            }

            if (!includeLargeOutputs && IsLargeOutputs(processedOutputs))
            {
                cellData["outputs"] = new JsonArray(
                    new JsonObject
                    {
                        ["output_type"] = "stream",
                        ["text"] = $"Outputs are too large to include. Use Bash with: cat <notebook_path> | jq '.cells[{index}].outputs'"
                    });
            }
            else
            {
                var serializedOutputs = new JsonArray();
                foreach (var output in processedOutputs)
                {
                    serializedOutputs.Add(output);
                }

                cellData["outputs"] = serializedOutputs;
            }
        }

        return cellData;
    }

    private static JsonObject ProcessOutput(JsonObject output)
    {
        var outputType = output["output_type"]?.GetValue<string>() ?? "stream";
        return outputType switch
        {
            "stream" => new JsonObject
            {
                ["output_type"] = outputType,
                ["text"] = FlattenString(output["text"])
            },
            "execute_result" or "display_data" => ProcessRichOutput(output, outputType),
            "error" => new JsonObject
            {
                ["output_type"] = outputType,
                ["text"] = BuildErrorText(output)
            },
            _ => new JsonObject
            {
                ["output_type"] = outputType,
                ["text"] = output.ToJsonString()
            }
        };
    }

    private static JsonObject ProcessRichOutput(JsonObject output, string outputType)
    {
        var result = new JsonObject
        {
            ["output_type"] = outputType,
            ["text"] = FlattenString(output["data"]?["text/plain"])
        };

        if (output["data"] is JsonObject data && ExtractImage(data) is JsonObject image)
        {
            result["image"] = image;
        }

        return result;
    }

    private static string BuildErrorText(JsonObject output)
    {
        var ename = output["ename"]?.GetValue<string>() ?? "Error";
        var evalue = output["evalue"]?.GetValue<string>() ?? string.Empty;
        var traceback = output["traceback"] is JsonArray tracebackArray
            ? string.Join('\n', tracebackArray.Select(FlattenString))
            : string.Empty;

        return string.IsNullOrEmpty(traceback)
            ? $"{ename}: {evalue}"
            : $"{ename}: {evalue}\n{traceback}";
    }

    private static JsonObject? ExtractImage(JsonObject data)
    {
        if (data["image/png"]?.GetValue<string>() is { Length: > 0 } png)
        {
            return new JsonObject
            {
                ["image_data"] = RemoveWhitespace(png),
                ["media_type"] = "image/png"
            };
        }

        if (data["image/jpeg"]?.GetValue<string>() is { Length: > 0 } jpeg)
        {
            return new JsonObject
            {
                ["image_data"] = RemoveWhitespace(jpeg),
                ["media_type"] = "image/jpeg"
            };
        }

        return null;
    }

    private static bool IsLargeOutputs(IReadOnlyCollection<JsonObject> outputs)
    {
        var size = 0;
        foreach (var output in outputs)
        {
            size += output["text"]?.GetValue<string>()?.Length ?? 0;
            size += output["image"]?["image_data"]?.GetValue<string>()?.Length ?? 0;
            if (size > LargeOutputThreshold)
            {
                return true;
            }
        }

        return false;
    }

    private static string FlattenString(JsonNode? node)
    {
        return node switch
        {
            null => string.Empty,
            JsonValue value when value.TryGetValue<string>(out var single) => single ?? string.Empty,
            JsonArray array => string.Concat(array.Select(FlattenString)),
            _ => node.ToJsonString()
        };
    }

    private static string RemoveWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (!char.IsWhiteSpace(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        var kilobytes = bytes / 1024d;
        if (kilobytes < 1024)
        {
            return $"{kilobytes:0.#} KB";
        }

        return $"{kilobytes / 1024d:0.#} MB";
    }
}

internal sealed record NotebookReadResult(JsonArray Cells, string CellsJson);
