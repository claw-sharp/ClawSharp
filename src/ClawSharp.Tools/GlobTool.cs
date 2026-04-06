// TS origin: ./tools/GlobTool/GlobTool.ts, ./utils/glob.ts
using System.Text.Json;
using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Tools;

internal sealed class GlobTool : BaseTool
{
    private const int DefaultLimit = 100;

    public GlobTool()
        : base(
            new ToolDescriptor(
                "Glob",
                "Fast file pattern matching tool that works with any codebase size",
                SearchHint: "find files by name pattern or wildcard",
                InputSchema: SearchToolSchemas.GlobInputSchema,
                OutputSchema: SearchToolSchemas.GlobOutputSchema,
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
            return ToolValidationResult.Invalid(errorMessage ?? "Glob requires a pattern.");
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
            return ToolValidationResult.Invalid(permissionResolution.Message ?? "Glob permission denied.");
        }

        if (!Directory.Exists(permissionResolution.ResolvedPath))
        {
            return ToolValidationResult.Invalid($"Directory does not exist: {searchPath}");
        }

        return ToolValidationResult.Valid();
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!TryParseInput(context.Arguments, out var input, out var errorMessage) || input is null)
        {
            return Failure(errorMessage ?? "Glob requires a pattern.");
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
            return Failure(permissionResolution.Message ?? "Glob permission denied.");
        }

        if (!Directory.Exists(permissionResolution.ResolvedPath))
        {
            return Failure($"Directory does not exist: {searchPath}");
        }

        var start = DateTimeOffset.UtcNow;
        IReadOnlyList<string> results;
        try
        {
            results = await RipgrepRunner.RunLinesAsync(
                [
                    "--files",
                    "--glob",
                    input.Pattern,
                    "--sort=modified",
                    "--no-ignore",
                    "--hidden"
                ],
                permissionResolution.ResolvedPath,
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return Failure(exception.Message);
        }

        var absoluteResults = results
            .Select(path => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(permissionResolution.ResolvedPath, path)))
            .ToArray();
        var truncated = absoluteResults.Length > DefaultLimit;
        var finalResults = absoluteResults.Take(DefaultLimit).Select(path => ToDisplayPath(context.WorkspaceRoot, path)).ToArray();
        var durationMs = (int)(DateTimeOffset.UtcNow - start).TotalMilliseconds;

        var filenames = new JsonArray();
        foreach (var result in finalResults)
        {
            filenames.Add(result);
        }

        var structured = new JsonObject
        {
            ["durationMs"] = durationMs,
            ["numFiles"] = finalResults.Length,
            ["filenames"] = filenames,
            ["truncated"] = truncated
        };

        if (finalResults.Length == 0)
        {
            return Success("No files found", structured);
        }

        var output = string.Join('\n', finalResults);
        if (truncated)
        {
            output += "\n(Results are truncated. Consider using a more specific path or pattern.)";
        }

        return Success(output, structured);
    }

    private static bool TryParseInput(string arguments, out GlobInput? input, out string? errorMessage)
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
            errorMessage = "Glob input must be a JSON object.";
            return false;
        }

        var pattern = jsonObject["pattern"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(pattern))
        {
            errorMessage = "Glob requires a non-empty pattern.";
            return false;
        }

        input = new GlobInput(pattern, jsonObject["path"]?.GetValue<string>());
        return true;
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

    private sealed record GlobInput(string Pattern, string? Path);
}
