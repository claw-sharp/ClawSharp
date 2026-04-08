// TS parity status: TS-shaped schema metadata, permission-governed path resolution, structured diff output, write-path metadata, encoding, stale-write guards, file-history snapshot storage, and file-update notification foundations are ported; full 1:1 parity still depends on richer file-history lifecycle semantics, concrete editor notification integrations, and the broader permission prompt runtime.
using ClawSharp.Core;
using System.Text.Json.Nodes;

namespace ClawSharp.Tools;

internal sealed class WriteTool : BaseTool
{
    public WriteTool()
        : base(
            new ToolDescriptor(
                "Write",
                "Write new files",
                Parameters:
                [
                    new ToolParameter("path", "Relative path to the file to write"),
                    new ToolParameter("content", "Content to write after the first | separator")
                ],
                InputSchema: FileToolSchemas.WriteInputSchema,
                OutputSchema: FileToolSchemas.WriteOutputSchema,
                Strict: true))
    {
    }

    public override async Task<ToolValidationResult> ValidateAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!FileToolInputParser.TryParseWrite(context.Arguments, out var input, out var errorMessage) || input is null)
        {
            return ToolValidationResult.Invalid(errorMessage ?? "Write requires input.");
        }

        var permissionResolution = await FileToolPermissionEvaluator.ResolveForWriteAsync(
            input.FilePath,
            context.WorkspaceRoot,
            context.ToolPermissionContext,
            context.PermissionPrompter,
            allowCreate: true,
            cancellationToken);
        if (!permissionResolution.Allowed)
        {
            return ToolValidationResult.Invalid(permissionResolution.Message ?? "Write permission denied.");
        }

        return ToolValidationResult.Valid();
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!FileToolInputParser.TryParseWrite(context.Arguments, out var input, out var errorMessage) || input is null)
        {
            return Failure(errorMessage ?? "Write requires input.");
        }

        var relativePath = input.FilePath;
        var content = input.Content;
        var permissionResolution = await FileToolPermissionEvaluator.ResolveForWriteAsync(
            relativePath,
            context.WorkspaceRoot,
            context.ToolPermissionContext,
            context.PermissionPrompter,
            allowCreate: true,
            cancellationToken);
        if (!permissionResolution.Allowed || permissionResolution.ResolvedPath is null)
        {
            return Failure(permissionResolution.Message ?? "Write permission denied.");
        }

        var resolvedPath = permissionResolution.ResolvedPath;
        await context.FileUpdateNotifier.BeforeFileEditedAsync(resolvedPath, cancellationToken);

        FileTextMetadata? metadata = null;
        string? oldContent = null;
        if (File.Exists(resolvedPath))
        {
            metadata = await Task.Run(() => FileTextOperations.ReadFileWithMetadata(resolvedPath), cancellationToken);
            oldContent = metadata.Content;
            var lastRead = context.ReadFileState.Get(resolvedPath);
            if (lastRead is null || lastRead.IsPartialView)
            {
                return Failure("File has not been read yet. Read it first before writing to it.");
            }

            var lastWriteTime = GetFileModificationTime(resolvedPath);
            if (lastWriteTime > lastRead.Timestamp)
            {
                var isFullRead = lastRead.Offset is null && lastRead.Limit is null;
                if (!isFullRead || !string.Equals(oldContent, lastRead.Content, StringComparison.Ordinal))
                {
                    return Failure(FileToolErrors.FileUnexpectedlyModified);
                }
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath)!);
        var encoding = metadata?.Encoding ?? new System.Text.UTF8Encoding(false);
        await FileHistoryService.TrackEditAsync(context, resolvedPath, cancellationToken);
        await Task.Run(
            () => FileTextOperations.WriteTextContent(resolvedPath, content, encoding, FileLineEnding.LF),
            cancellationToken);
        await context.FileUpdateNotifier.ClearDiagnosticsForFileAsync(resolvedPath, cancellationToken);
        await context.FileUpdateNotifier.NotifyFileUpdatedAsync(resolvedPath, oldContent, content, cancellationToken);
        context.ReadFileState.Set(
            resolvedPath,
            new FileState(
                content,
                GetFileModificationTime(resolvedPath),
                Offset: null,
                Limit: null));

        var structuredOutput = new JsonObject
        {
            ["type"] = oldContent is null ? "create" : "update",
            ["filePath"] = relativePath,
            ["content"] = content,
            ["structuredPatch"] = oldContent is null
                ? new JsonArray()
                : FileStructuredPatchBuilder.Build(oldContent, content),
            ["originalFile"] = oldContent is null ? null : JsonValue.Create(oldContent)
        };

        return Success(

            oldContent is null
                ? $"Created {relativePath}."
                : $"Updated {relativePath}.",
            structuredOutput);
    }

    private static long GetFileModificationTime(string filePath)
    {
        return new DateTimeOffset(File.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds();
    }
}
