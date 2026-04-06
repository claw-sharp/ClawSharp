// TS origin: ./tools/FileEditTool/FileEditTool.ts
// TS parity status: TS-shaped schema metadata, permission-governed path resolution, structured diff output, read-before-write guards, text metadata handling, file-history snapshot storage, and file-update notification foundations are ported; full 1:1 parity still depends on richer file-history lifecycle semantics, concrete editor notification integrations, and the broader permission prompt runtime.
using ClawSharp.Core;
using System.Text.Json.Nodes;

namespace ClawSharp.Tools;

internal sealed class EditTool : BaseTool
{
    public EditTool()
        : base(
            new ToolDescriptor(
                "Edit",
                "Edit files in the workspace",
                Parameters:
                [
                    new ToolParameter("path", "Relative path to the file to edit"),
                    new ToolParameter("search", "Existing text before the second | separator"),
                    new ToolParameter("replace", "Replacement text after the second | separator")
                ],
                InputSchema: FileToolSchemas.EditInputSchema,
                OutputSchema: FileToolSchemas.EditOutputSchema,
                Strict: true))
    {
    }

    public override async Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!FileToolInputParser.TryParseEdit(context.Arguments, out var input, out var errorMessage) || input is null)
        {
            return ToolValidationResult.Invalid(errorMessage ?? "Edit requires input.");
        }

        var permissionResolution = await FileToolPermissionEvaluator.ResolveForWriteAsync(
            input.FilePath,
            context.WorkspaceRoot,
            context.ToolPermissionContext,
            context.PermissionPrompter,
            allowCreate: false,
            cancellationToken);
        if (!permissionResolution.Allowed)
        {
            return ToolValidationResult.Invalid(permissionResolution.Message ?? "Edit permission denied.");
        }

        return ToolValidationResult.Valid();
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!FileToolInputParser.TryParseEdit(context.Arguments, out var input, out var errorMessage) || input is null)
        {
            return Failure(errorMessage ?? "Edit requires input.");
        }

        var permissionResolution = await FileToolPermissionEvaluator.ResolveForWriteAsync(
            input.FilePath,
            context.WorkspaceRoot,
            context.ToolPermissionContext,
            context.PermissionPrompter,
            allowCreate: false,
            cancellationToken);
        if (!permissionResolution.Allowed || permissionResolution.ResolvedPath is null)
        {
            return Failure(permissionResolution.Message ?? "Edit permission denied.");
        }

        var resolvedPath = permissionResolution.ResolvedPath;
        await context.FileUpdateNotifier.BeforeFileEditedAsync(resolvedPath, cancellationToken);
        if (!File.Exists(resolvedPath))
        {
            return Failure($"Edit could not find '{input.FilePath}'.");
        }

        var metadata = await Task.Run(() => FileTextOperations.ReadFileWithMetadata(resolvedPath), cancellationToken);
        var original = metadata.Content;
        var lastRead = context.ReadFileState.Get(resolvedPath);
        if (lastRead is null || lastRead.IsPartialView)
        {
            return Failure("File has not been read yet. Read it first before writing to it.");
        }

        var lastWriteTime = GetFileModificationTime(resolvedPath);
        if (lastWriteTime > lastRead.Timestamp)
        {
            var isFullRead = lastRead.Offset is null && lastRead.Limit is null;
            if (!isFullRead || !string.Equals(original, lastRead.Content, StringComparison.Ordinal))
            {
                return Failure("File has been modified since read, either by the user or by a linter. Read it again before attempting to write it.");
            }
        }

        if (!original.Contains(input.OldString, StringComparison.Ordinal))
        {
            return Failure("Edit could not find the requested search text.");
        }

        var updated = input.ReplaceAll
            ? original.Replace(input.OldString, input.NewString, StringComparison.Ordinal)
            : ReplaceFirst(original, input.OldString, input.NewString);
        var structuredPatch = FileStructuredPatchBuilder.Build(original, updated);
        await FileHistoryService.TrackEditAsync(context, resolvedPath, cancellationToken);
        await Task.Run(
            () => FileTextOperations.WriteTextContent(resolvedPath, updated, metadata.Encoding, metadata.LineEndings),
            cancellationToken);
        await context.FileUpdateNotifier.ClearDiagnosticsForFileAsync(resolvedPath, cancellationToken);
        await context.FileUpdateNotifier.NotifyFileUpdatedAsync(resolvedPath, original, updated, cancellationToken);
        context.ReadFileState.Set(
            resolvedPath,
            new FileState(
                updated,
                GetFileModificationTime(resolvedPath),
                Offset: null,
                Limit: null));
        return Success(
            $"Edited {input.FilePath} successfully.",
            new JsonObject
            {
                ["filePath"] = input.FilePath,
                ["oldString"] = input.OldString,
                ["newString"] = input.NewString,
                ["originalFile"] = original,
                ["structuredPatch"] = structuredPatch,
                ["userModified"] = false,
                ["replaceAll"] = input.ReplaceAll
            });
    }

    private static long GetFileModificationTime(string filePath)
    {
        return new DateTimeOffset(File.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds();
    }

    private static string ReplaceFirst(string content, string oldString, string newString)
    {
        var index = content.IndexOf(oldString, StringComparison.Ordinal);
        return index < 0
            ? content
            : string.Concat(content.AsSpan(0, index), newString, content.AsSpan(index + oldString.Length));
    }
}
