using System.Text.Json;

namespace ClawSharp.Tools;

internal static class FileToolInputParser
{
    public static bool TryParseRead(
        string arguments,
        out ReadToolInput? input,
        out string? errorMessage)
    {
        input = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(arguments))
        {
            errorMessage = "Read requires a file path.";
            return false;
        }

        if (!LooksLikeJson(arguments))
        {
            input = new ReadToolInput(arguments.Trim(), 1, null, null);
            return true;
        }

        try
        {
            var document = JsonDocument.Parse(arguments);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                errorMessage = "Read requires a JSON object input.";
                return false;
            }

            var root = document.RootElement;
            if (!root.TryGetProperty("file_path", out var filePathElement) ||
                filePathElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(filePathElement.GetString()))
            {
                errorMessage = "Read requires 'file_path'.";
                return false;
            }

            var offset = 1;
            if (root.TryGetProperty("offset", out var offsetElement) && offsetElement.ValueKind != JsonValueKind.Null)
            {
                if (!TryReadInteger(offsetElement, out offset) || offset < 0)
                {
                    errorMessage = "Read requires 'offset' to be a non-negative integer.";
                    return false;
                }
            }

            int? limit = null;
            if (root.TryGetProperty("limit", out var limitElement) && limitElement.ValueKind != JsonValueKind.Null)
            {
                if (!TryReadInteger(limitElement, out var parsedLimit) || parsedLimit <= 0)
                {
                    errorMessage = "Read requires 'limit' to be a positive integer.";
                    return false;
                }

                limit = parsedLimit;
            }

            string? pages = null;
            if (root.TryGetProperty("pages", out var pagesElement) && pagesElement.ValueKind != JsonValueKind.Null)
            {
                if (pagesElement.ValueKind != JsonValueKind.String)
                {
                    errorMessage = "Read requires 'pages' to be a string when provided.";
                    return false;
                }

                pages = pagesElement.GetString();
            }

            input = new ReadToolInput(filePathElement.GetString()!.Trim(), offset == 0 ? 1 : offset, limit, pages);
            return true;
        }
        catch (JsonException)
        {
            errorMessage = "Read requires valid JSON input.";
            return false;
        }
    }

    public static bool TryParseEdit(
        string arguments,
        out EditToolInput? input,
        out string? errorMessage)
    {
        input = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(arguments))
        {
            errorMessage = "Edit requires input.";
            return false;
        }

        if (!LooksLikeJson(arguments))
        {
            var parts = arguments.Split('|', 3, StringSplitOptions.None);
            if (parts.Length != 3 || string.IsNullOrWhiteSpace(parts[0]))
            {
                errorMessage = "Edit requires 'relative/path|search|replace'.";
                return false;
            }

            input = new EditToolInput(parts[0].Trim(), parts[1], parts[2], ReplaceAll: false);
            return true;
        }

        try
        {
            var document = JsonDocument.Parse(arguments);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                errorMessage = "Edit requires a JSON object input.";
                return false;
            }

            var root = document.RootElement;
            if (!TryReadRequiredString(root, "file_path", out var filePath) ||
                !TryReadRequiredString(root, "old_string", out var oldString) ||
                !TryReadRequiredString(root, "new_string", out var newString))
            {
                errorMessage = "Edit requires 'file_path', 'old_string', and 'new_string'.";
                return false;
            }

            var replaceAll = false;
            if (root.TryGetProperty("replace_all", out var replaceAllElement) && replaceAllElement.ValueKind != JsonValueKind.Null)
            {
                if (replaceAllElement.ValueKind != JsonValueKind.True && replaceAllElement.ValueKind != JsonValueKind.False)
                {
                    errorMessage = "Edit requires 'replace_all' to be a boolean when provided.";
                    return false;
                }

                replaceAll = replaceAllElement.GetBoolean();
            }

            input = new EditToolInput(filePath.Trim(), oldString, newString, replaceAll);
            return true;
        }
        catch (JsonException)
        {
            errorMessage = "Edit requires valid JSON input.";
            return false;
        }
    }

    public static bool TryParseWrite(
        string arguments,
        out WriteToolInput? input,
        out string? errorMessage)
    {
        input = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(arguments))
        {
            errorMessage = "Write requires input.";
            return false;
        }

        if (!LooksLikeJson(arguments))
        {
            var parts = arguments.Split('|', 2, StringSplitOptions.None);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]))
            {
                errorMessage = "Write requires 'relative/path|content'.";
                return false;
            }

            input = new WriteToolInput(parts[0].Trim(), parts[1]);
            return true;
        }

        try
        {
            var document = JsonDocument.Parse(arguments);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                errorMessage = "Write requires a JSON object input.";
                return false;
            }

            var root = document.RootElement;
            if (!TryReadRequiredString(root, "file_path", out var filePath) ||
                !TryReadRequiredString(root, "content", out var content))
            {
                errorMessage = "Write requires 'file_path' and 'content'.";
                return false;
            }

            input = new WriteToolInput(filePath.Trim(), content);
            return true;
        }
        catch (JsonException)
        {
            errorMessage = "Write requires valid JSON input.";
            return false;
        }
    }

    private static bool LooksLikeJson(string value)
    {
        return value.TrimStart().StartsWith("{", StringComparison.Ordinal);
    }

    private static bool TryReadRequiredString(JsonElement root, string propertyName, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryReadInteger(JsonElement element, out int value)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetInt32(out value);
        }

        value = 0;
        return false;
    }
}

internal sealed record ReadToolInput(string FilePath, int Offset, int? Limit, string? Pages);

internal sealed record EditToolInput(string FilePath, string OldString, string NewString, bool ReplaceAll);

internal sealed record WriteToolInput(string FilePath, string Content);
