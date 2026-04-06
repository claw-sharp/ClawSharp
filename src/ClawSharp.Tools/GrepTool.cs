// TS origin: ./tools/GrepTool/GrepTool.ts, ./utils/ripgrep.ts
using System.Text.Json;
using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Tools;

internal sealed class GrepTool : BaseTool
{
    private const int DefaultHeadLimit = 250;

    public GrepTool()
        : base(
            new ToolDescriptor(
                "Grep",
                "A powerful search tool built on ripgrep",
                SearchHint: "search file contents with regex (ripgrep)",
                InputSchema: SearchToolSchemas.GrepInputSchema,
                OutputSchema: SearchToolSchemas.GrepOutputSchema,
                Strict: true))
    {
    }

    public override bool IsConcurrencySafe(string arguments)
    {
        return true;
    }

    public override bool IsReadOnly(string arguments)
    {
        return true;
    }

    public override async Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!TryParseInput(context.Arguments, out var input, out var errorMessage) || input is null)
        {
            return ToolValidationResult.Invalid(errorMessage ?? "Grep requires a pattern.");
        }

        var searchPath = string.IsNullOrWhiteSpace(input.Path) ? context.WorkspaceRoot : input.Path;
        var permissionResolution = await FileToolPermissionEvaluator.ResolveForReadAsync(
            searchPath,
            context.WorkspaceRoot,
            context.ToolPermissionContext,
            context.PermissionPrompter,
            cancellationToken);
        if (!permissionResolution.Allowed || permissionResolution.ResolvedPath is null)
        {
            return ToolValidationResult.Invalid(permissionResolution.Message ?? "Grep permission denied.");
        }

        if (!File.Exists(permissionResolution.ResolvedPath) && !Directory.Exists(permissionResolution.ResolvedPath))
        {
            return ToolValidationResult.Invalid($"Path does not exist: {searchPath}");
        }

        return ToolValidationResult.Valid();
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!TryParseInput(context.Arguments, out var input, out var errorMessage) || input is null)
        {
            return Failure(errorMessage ?? "Grep requires a pattern.");
        }

        var searchPath = string.IsNullOrWhiteSpace(input.Path) ? context.WorkspaceRoot : input.Path;
        var permissionResolution = await FileToolPermissionEvaluator.ResolveForReadAsync(
            searchPath,
            context.WorkspaceRoot,
            context.ToolPermissionContext,
            context.PermissionPrompter,
            cancellationToken);
        if (!permissionResolution.Allowed || permissionResolution.ResolvedPath is null)
        {
            return Failure(permissionResolution.Message ?? "Grep permission denied.");
        }

        if (!File.Exists(permissionResolution.ResolvedPath) && !Directory.Exists(permissionResolution.ResolvedPath))
        {
            return Failure($"Path does not exist: {searchPath}");
        }

        IReadOnlyList<string> results;
        try
        {
            results = await RipgrepRunner.RunLinesAsync(
                BuildArguments(input),
                permissionResolution.ResolvedPath,
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return Failure(exception.Message);
        }

        return input.OutputMode switch
        {
            "content" => BuildContentResult(results, permissionResolution.ResolvedPath, context.WorkspaceRoot, input),
            "count" => BuildCountResult(results, permissionResolution.ResolvedPath, context.WorkspaceRoot, input),
            _ => BuildFilesResult(results, permissionResolution.ResolvedPath, context.WorkspaceRoot, input)
        };
    }

    private static IReadOnlyList<string> BuildArguments(GrepInput input)
    {
        var args = new List<string>
        {
            "--hidden",
            "--max-columns",
            "500",
            "-H"
        };

        if (input.Multiline)
        {
            args.Add("-U");
            args.Add("--multiline-dotall");
        }

        if (input.CaseInsensitive)
        {
            args.Add("-i");
        }

        if (input.OutputMode == "files_with_matches")
        {
            args.Add("-l");
        }
        else if (input.OutputMode == "count")
        {
            args.Add("-c");
        }

        if (input.ShowLineNumbers && input.OutputMode == "content")
        {
            args.Add("-n");
        }

        if (input.OutputMode == "content")
        {
            if (input.Context is not null)
            {
                args.Add("-C");
                args.Add(input.Context.Value.ToString());
            }
            else if (input.ContextAlias is not null)
            {
                args.Add("-C");
                args.Add(input.ContextAlias.Value.ToString());
            }
            else
            {
                if (input.BeforeContext is not null)
                {
                    args.Add("-B");
                    args.Add(input.BeforeContext.Value.ToString());
                }

                if (input.AfterContext is not null)
                {
                    args.Add("-A");
                    args.Add(input.AfterContext.Value.ToString());
                }
            }
        }

        if (input.Pattern.StartsWith("-", StringComparison.Ordinal))
        {
            args.Add("-e");
            args.Add(input.Pattern);
        }
        else
        {
            args.Add(input.Pattern);
        }

        if (!string.IsNullOrWhiteSpace(input.Type))
        {
            args.Add("--type");
            args.Add(input.Type);
        }

        foreach (var globPattern in ExpandGlobPatterns(input.Glob))
        {
            args.Add("--glob");
            args.Add(globPattern);
        }

        return args;
    }

    private static ToolExecutionResult BuildContentResult(
        IReadOnlyList<string> results,
        string resolvedPath,
        string workspaceRoot,
        GrepInput input)
    {
        var relativeLines = results
            .Select(line => RelativizePathPrefix(line, resolvedPath, workspaceRoot))
            .ToArray();
        var paged = ApplyHeadLimit(relativeLines, input.HeadLimit, input.Offset);
        var content = string.Join('\n', paged.Items);
        var structured = new JsonObject
        {
            ["mode"] = "content",
            ["numFiles"] = 0,
            ["filenames"] = new JsonArray(),
            ["content"] = content,
            ["numLines"] = paged.Items.Length
        };
        if (paged.AppliedLimit is not null)
        {
            structured["appliedLimit"] = paged.AppliedLimit.Value;
        }

        if (input.Offset > 0)
        {
            structured["appliedOffset"] = input.Offset;
        }

        var output = string.IsNullOrWhiteSpace(content) ? "No matches found" : content;
        if (paged.AppliedLimit is not null || input.Offset > 0)
        {
            output += $"\n\n[Showing results with pagination = {FormatLimitInfo(paged.AppliedLimit, input.Offset)}]";
        }

        return Success(output, structured);
    }

    private static ToolExecutionResult BuildCountResult(
        IReadOnlyList<string> results,
        string resolvedPath,
        string workspaceRoot,
        GrepInput input)
    {
        var relativeLines = results
            .Select(line => RelativizePathPrefix(line, resolvedPath, workspaceRoot))
            .ToArray();
        var paged = ApplyHeadLimit(relativeLines, input.HeadLimit, input.Offset);

        var totalMatches = 0;
        var fileCount = 0;
        foreach (var line in paged.Items)
        {
            var colonIndex = line.LastIndexOf(':');
            if (colonIndex <= 0)
            {
                continue;
            }

            if (int.TryParse(line[(colonIndex + 1)..], out var count))
            {
                totalMatches += count;
                fileCount += 1;
            }
        }

        var content = string.Join('\n', paged.Items);
        var structured = new JsonObject
        {
            ["mode"] = "count",
            ["numFiles"] = fileCount,
            ["filenames"] = new JsonArray(),
            ["content"] = content,
            ["numMatches"] = totalMatches
        };
        if (paged.AppliedLimit is not null)
        {
            structured["appliedLimit"] = paged.AppliedLimit.Value;
        }

        if (input.Offset > 0)
        {
            structured["appliedOffset"] = input.Offset;
        }

        var output = string.IsNullOrWhiteSpace(content) ? "No matches found" : content;
        output += $"\n\nFound {totalMatches} total {(totalMatches == 1 ? "occurrence" : "occurrences")} across {fileCount} {(fileCount == 1 ? "file" : "files")}.";
        if (paged.AppliedLimit is not null || input.Offset > 0)
        {
            output += $" with pagination = {FormatLimitInfo(paged.AppliedLimit, input.Offset)}";
        }

        return Success(output, structured);
    }

    private static ToolExecutionResult BuildFilesResult(
        IReadOnlyList<string> results,
        string resolvedPath,
        string workspaceRoot,
        GrepInput input)
    {
        var sorted = results
            .Select(path => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(resolvedPath) ?? resolvedPath, path)))
            .OrderByDescending(GetLastWriteTimeOrMin)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var paged = ApplyHeadLimit(sorted, input.HeadLimit, input.Offset);
        var relativeMatches = paged.Items.Select(path => ToDisplayPath(workspaceRoot, path)).ToArray();

        var filenames = new JsonArray();
        foreach (var relativeMatch in relativeMatches)
        {
            filenames.Add(relativeMatch);
        }

        var structured = new JsonObject
        {
            ["mode"] = "files_with_matches",
            ["numFiles"] = relativeMatches.Length,
            ["filenames"] = filenames
        };
        if (paged.AppliedLimit is not null)
        {
            structured["appliedLimit"] = paged.AppliedLimit.Value;
        }

        if (input.Offset > 0)
        {
            structured["appliedOffset"] = input.Offset;
        }

        if (relativeMatches.Length == 0)
        {
            return Success("No files found", structured);
        }

        var prefix = $"Found {relativeMatches.Length} {(relativeMatches.Length == 1 ? "file" : "files")}";
        var limitInfo = FormatLimitInfo(paged.AppliedLimit, input.Offset);
        if (!string.IsNullOrWhiteSpace(limitInfo))
        {
            prefix += $" {limitInfo}";
        }

        return Success($"{prefix}\n{string.Join('\n', relativeMatches)}", structured);
    }

    private static (T[] Items, int? AppliedLimit) ApplyHeadLimit<T>(IReadOnlyList<T> items, int? limit, int offset)
    {
        if (limit == 0)
        {
            return (items.Skip(offset).ToArray(), null);
        }

        var effectiveLimit = limit ?? DefaultHeadLimit;
        var sliced = items.Skip(offset).Take(effectiveLimit).ToArray();
        var wasTruncated = items.Count - offset > effectiveLimit;
        return (sliced, wasTruncated ? effectiveLimit : null);
    }

    private static IEnumerable<string> ExpandGlobPatterns(string? glob)
    {
        if (string.IsNullOrWhiteSpace(glob))
        {
            yield break;
        }

        foreach (var rawPattern in glob.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawPattern.Contains('{', StringComparison.Ordinal) && rawPattern.Contains('}', StringComparison.Ordinal))
            {
                yield return rawPattern;
                continue;
            }

            foreach (var splitPattern in rawPattern.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return splitPattern;
            }
        }
    }

    private static string RelativizePathPrefix(string line, string resolvedPath, string workspaceRoot)
    {
        var colonIndex = line.IndexOf(':');
        if (colonIndex <= 0)
        {
            return line;
        }

        var filePath = line[..colonIndex];
        var rest = line[colonIndex..];
        var absolutePath = Path.IsPathRooted(filePath)
            ? filePath
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(resolvedPath) ?? resolvedPath, filePath));
        return $"{ToDisplayPath(workspaceRoot, absolutePath)}{rest}";
    }

    private static DateTime GetLastWriteTimeOrMin(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static string ToDisplayPath(string workspaceRoot, string path)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(workspaceRoot);
        if (fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetRelativePath(fullRoot, fullPath).Replace('\\', '/');
        }

        return fullPath.Replace('\\', '/');
    }

    private static string FormatLimitInfo(int? appliedLimit, int appliedOffset)
    {
        var parts = new List<string>();
        if (appliedLimit is not null)
        {
            parts.Add($"limit: {appliedLimit.Value}");
        }

        if (appliedOffset > 0)
        {
            parts.Add($"offset: {appliedOffset}");
        }

        return string.Join(", ", parts);
    }

    private static bool TryParseInput(string arguments, out GrepInput? input, out string? errorMessage)
    {
        input = null;
        errorMessage = null;

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(arguments);
        }
        catch (JsonException exception)
        {
            errorMessage = $"Invalid JSON input: {exception.Message}";
            return false;
        }

        if (node is not JsonObject jsonObject)
        {
            errorMessage = "Grep input must be a JSON object.";
            return false;
        }

        var pattern = jsonObject["pattern"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(pattern))
        {
            errorMessage = "Grep requires a non-empty pattern.";
            return false;
        }

        input = new GrepInput(
            pattern,
            jsonObject["path"]?.GetValue<string>(),
            jsonObject["glob"]?.GetValue<string>(),
            jsonObject["output_mode"]?.GetValue<string>() ?? "files_with_matches",
            GetOptionalInt(jsonObject, "-B"),
            GetOptionalInt(jsonObject, "-A"),
            GetOptionalInt(jsonObject, "-C"),
            GetOptionalInt(jsonObject, "context"),
            GetOptionalBool(jsonObject, "-n") ?? true,
            GetOptionalBool(jsonObject, "-i") ?? false,
            jsonObject["type"]?.GetValue<string>(),
            GetOptionalInt(jsonObject, "head_limit"),
            GetOptionalInt(jsonObject, "offset") ?? 0,
            GetOptionalBool(jsonObject, "multiline") ?? false);
        return true;
    }

    private static int? GetOptionalInt(JsonObject jsonObject, string key)
    {
        if (jsonObject[key] is null)
        {
            return null;
        }

        if (jsonObject[key] is JsonValue value)
        {
            if (value.TryGetValue<int>(out var intValue))
            {
                return intValue;
            }

            if (value.TryGetValue<string>(out var stringValue) && int.TryParse(stringValue, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static bool? GetOptionalBool(JsonObject jsonObject, string key)
    {
        if (jsonObject[key] is null)
        {
            return null;
        }

        if (jsonObject[key] is JsonValue value)
        {
            if (value.TryGetValue<bool>(out var boolValue))
            {
                return boolValue;
            }

            if (value.TryGetValue<string>(out var stringValue) && bool.TryParse(stringValue, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private sealed record GrepInput(
        string Pattern,
        string? Path,
        string? Glob,
        string OutputMode,
        int? BeforeContext,
        int? AfterContext,
        int? ContextAlias,
        int? Context,
        bool ShowLineNumbers,
        bool CaseInsensitive,
        string? Type,
        int? HeadLimit,
        int Offset,
        bool Multiline);
}
