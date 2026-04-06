// TS origin: ./tools/FileReadTool/FileReadTool.ts
// TS parity status: TS-shaped schema metadata, permission-governed path resolution, image, notebook, full-PDF, and explicit PDF page-extraction branches, file-unchanged dedup, byte-size guards, blocked binary/device-file checks, token-budget enforcement foundation, and normalized read-file-state handling are ported; full 1:1 parity still depends on exact API token counting, image resize/compression, PDF image-part delivery into the model-backed runtime, and the broader permission prompt runtime.
using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Tools;

internal sealed class ReadTool : BaseTool
{
    public ReadTool()
        : base(
            new ToolDescriptor(
                "Read",
                "Read files and resources",
                Parameters:
                [
                    new ToolParameter("path", "Relative path to the file to read")
                ],
                InputSchema: FileToolSchemas.ReadInputSchema,
                OutputSchema: FileToolSchemas.ReadOutputSchema,
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
        if (!FileToolInputParser.TryParseRead(context.Arguments, out var input, out var errorMessage) || input is null)
        {
            return ToolValidationResult.Invalid(errorMessage ?? "Read requires a file path.");
        }

        var permissionResolution = await FileToolPermissionEvaluator.ResolveForReadAsync(
            input.FilePath,
            context.WorkspaceRoot,
            context.ToolPermissionContext,
            context.PermissionPrompter,
            cancellationToken);
        if (!permissionResolution.Allowed || permissionResolution.ResolvedPath is null)
        {
            return ToolValidationResult.Invalid(permissionResolution.Message ?? "Read permission denied.");
        }

        var resolvedPath = permissionResolution.ResolvedPath;
        if (input.Pages is not null)
        {
            if (!ReadToolPolicies.TryParsePdfPageRange(input.Pages, out var range) || range is null)
            {
                return ToolValidationResult.Invalid(
                    $"Invalid pages parameter: \"{input.Pages}\". Use formats like \"1-5\", \"3\", or \"10-20\". Pages are 1-indexed.");
            }

            var rangeSize = range.LastPage == int.MaxValue
                ? ReadToolPolicies.PdfMaxPagesPerRead + 1
                : range.LastPage - range.FirstPage + 1;
            if (rangeSize > ReadToolPolicies.PdfMaxPagesPerRead)
            {
                return ToolValidationResult.Invalid(
                    $"Page range \"{input.Pages}\" exceeds maximum of {ReadToolPolicies.PdfMaxPagesPerRead} pages per request. Please use a smaller range.");
            }
        }

        return ToolValidationResult.Valid();
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!FileToolInputParser.TryParseRead(context.Arguments, out var input, out var errorMessage) || input is null)
        {
            return Failure(errorMessage ?? "Read requires a file path.");
        }

        var permissionResolution = await FileToolPermissionEvaluator.ResolveForReadAsync(
            input.FilePath,
            context.WorkspaceRoot,
            context.ToolPermissionContext,
            context.PermissionPrompter,
            cancellationToken);
        if (!permissionResolution.Allowed || permissionResolution.ResolvedPath is null)
        {
            return Failure(permissionResolution.Message ?? "Read permission denied.");
        }

        var resolvedPath = permissionResolution.ResolvedPath;
        if (!File.Exists(resolvedPath))
        {
            var alternateScreenshotPath = ReadToolPathHints.GetAlternateScreenshotPath(resolvedPath);
            if (!string.IsNullOrWhiteSpace(alternateScreenshotPath) && File.Exists(alternateScreenshotPath))
            {
                resolvedPath = alternateScreenshotPath;
            }
            else
            {
                return Failure(ReadToolPathHints.BuildFileNotFoundMessage(resolvedPath, context.WorkspaceRoot));
            }
        }

        if (ReadToolPolicies.HasBlockedBinaryExtension(resolvedPath))
        {
            return Failure(
                $"This tool cannot read binary files. The file appears to be a binary {Path.GetExtension(resolvedPath)} file. Please use appropriate tools for binary file analysis.");
        }

        if (ReadToolPolicies.IsBlockedDevicePath(resolvedPath.Replace('\\', '/')))
        {
            return Failure(
                $"Cannot read '{input.FilePath}': this device file would block or produce infinite output.");
        }

        var existingState = context.ReadFileState.Get(resolvedPath);
        var lastWriteTime = GetFileModificationTime(resolvedPath);
        var requestedStartLine = input.Offset <= 1 ? 1 : input.Offset;
        var requestedLimit = input.Limit ?? ReadToolPolicies.MaxLinesToRead;
        var requestedIsDefaultFullRead = requestedStartLine == 1 && input.Limit is null;
        var existingMatchesRequestedRange =
            (existingState?.Offset == requestedStartLine && existingState?.Limit == requestedLimit) ||
            (requestedIsDefaultFullRead && existingState?.Offset is null && existingState?.Limit is null);
        if (existingState is not null &&
            !existingState.IsPartialView &&
            existingMatchesRequestedRange &&
            existingState.Timestamp == lastWriteTime)
        {
            return Success(
                ReadToolPolicies.FileUnchangedStub,
                new JsonObject
                {
                    ["type"] = "file_unchanged",
                    ["file"] = new JsonObject
                    {
                        ["filePath"] = input.FilePath
                    }
                });
        }

        var extension = Path.GetExtension(resolvedPath);
        if (string.Equals(extension, ".ipynb", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var notebook = await Task.Run(
                    () => ReadToolNotebookReader.Read(resolvedPath, (int)ReadToolPolicies.MaxSizeBytes),
                    cancellationToken);
                ReadToolTokenBudget.Validate(notebook.CellsJson, extension);
                context.ReadFileState.Set(
                    resolvedPath,
                    new FileState(
                        notebook.CellsJson,
                        lastWriteTime,
                        Offset: null,
                        Limit: null,
                        IsPartialView: false));

                return Success(
                    notebook.CellsJson,
                    new JsonObject
                    {
                        ["type"] = "notebook",
                        ["file"] = new JsonObject
                        {
                            ["filePath"] = input.FilePath,
                            ["cells"] = notebook.Cells.DeepClone()
                        }
                    });
            }
            catch (InvalidOperationException exception)
            {
                return Failure(exception.Message);
            }
        }

        if (ReadToolImageReader.IsSupportedImageExtension(resolvedPath))
        {
            try
            {
                var image = await Task.Run(() => ReadToolImageReader.Read(resolvedPath), cancellationToken);
                return Success(
                    $"Image file read: {input.FilePath} ({FormatFileSize(image.OriginalSize)})",
                    new JsonObject
                    {
                        ["type"] = "image",
                        ["file"] = new JsonObject
                        {
                            ["base64"] = image.Base64,
                            ["type"] = image.MediaType,
                            ["originalSize"] = image.OriginalSize
                        }
                    });
            }
            catch (InvalidOperationException exception)
            {
                return Failure(exception.Message);
            }
        }

        if (ReadToolPolicies.IsPdfExtension(resolvedPath))
        {
            if (input.Pages is not null)
            {
                try
                {
                    ReadToolPolicies.TryParsePdfPageRange(input.Pages, out var range);
                    var extractedPages = await ReadToolPdfPageExtractor.ExtractPagesAsync(
                        resolvedPath,
                        context.Session,
                        range,
                        cancellationToken);
                    return Success(
                        $"PDF pages extracted: {extractedPages.Count} page(s) from {input.FilePath} ({FormatFileSize(extractedPages.OriginalSize)})",
                        new JsonObject
                        {
                            ["type"] = "parts",
                            ["file"] = new JsonObject
                            {
                                ["filePath"] = input.FilePath,
                                ["originalSize"] = extractedPages.OriginalSize,
                                ["count"] = extractedPages.Count,
                                ["outputDir"] = extractedPages.OutputDir
                            }
                        });
                }
                catch (InvalidOperationException exception)
                {
                    return Failure(exception.Message);
                }
            }

            try
            {
                var pageCount = await ReadToolPdfPageExtractor.GetPdfPageCountAsync(resolvedPath, cancellationToken);
                if (pageCount is not null && pageCount > ReadToolPolicies.PdfAtMentionInlineThreshold)
                {
                    return Failure(
                        $"This PDF has {pageCount} pages, which is too many to read at once. Use the pages parameter to read specific page ranges (e.g., pages: \"1-5\"). Maximum {ReadToolPolicies.PdfMaxPagesPerRead} pages per request.");
                }

                var pdf = await Task.Run(() => ReadToolPdfReader.Read(resolvedPath), cancellationToken);
                return Success(
                    $"PDF file read: {input.FilePath} ({FormatFileSize(pdf.OriginalSize)})",
                    new JsonObject
                    {
                        ["type"] = "pdf",
                        ["file"] = new JsonObject
                        {
                            ["filePath"] = input.FilePath,
                            ["base64"] = pdf.Base64,
                            ["originalSize"] = pdf.OriginalSize
                        }
                    });
            }
            catch (InvalidOperationException exception)
            {
                return Failure(exception.Message);
            }
        }

        var fileInfo = new FileInfo(resolvedPath);
        if (input.Limit is null && fileInfo.Length > ReadToolPolicies.MaxSizeBytes)
        {
            return Failure(
                $"File content ({FormatFileSize(fileInfo.Length)}) exceeds maximum allowed size ({FormatFileSize(ReadToolPolicies.MaxSizeBytes)}). Use offset and limit to read specific portions of the file.");
        }

        try
        {
            var metadata = await Task.Run(() => FileTextOperations.ReadFileWithMetadata(resolvedPath), cancellationToken);
            var normalizedContent = metadata.Content;
            var totalLines = CountLines(normalizedContent);
            var startLine = requestedStartLine;
            var effectiveLimit = input.Limit ?? ReadToolPolicies.MaxLinesToRead;
            var selectedContent = SliceLines(normalizedContent, startLine, effectiveLimit);
            ReadToolTokenBudget.Validate(selectedContent, extension);
            var isRangeRead = startLine > 1 || input.Limit is not null;
            var isLineCappedRead = input.Limit is null && totalLines > ReadToolPolicies.MaxLinesToRead;
            var isPartialView = isRangeRead || isLineCappedRead || selectedContent.Length > ReadToolPolicies.MaxReturnedCharacters;
            context.ReadFileState.Set(
                resolvedPath,
                new FileState(
                    isPartialView ? selectedContent : normalizedContent,
                    GetFileModificationTime(resolvedPath),
                    Offset: isPartialView ? startLine : null,
                    Limit: isPartialView ? effectiveLimit : null,
                    IsPartialView: isPartialView));

            var output = selectedContent;
            if (output.Length > ReadToolPolicies.MaxReturnedCharacters)
            {
                output = output[..ReadToolPolicies.MaxReturnedCharacters] + Environment.NewLine + "[truncated]";
            }

            var structuredOutput = new JsonObject
            {
                ["type"] = "text",
                ["file"] = new JsonObject
                {
                    ["filePath"] = input.FilePath,
                    ["content"] = output,
                    ["numLines"] = CountLines(output),
                    ["startLine"] = startLine,
                    ["totalLines"] = totalLines
                }
            };

            return Success(output, structuredOutput);
        }
        catch (InvalidOperationException exception)
        {
            return Failure(exception.Message);
        }
    }

    private static long GetFileModificationTime(string filePath)
    {
        return new DateTimeOffset(File.GetLastWriteTimeUtc(filePath)).ToUnixTimeMilliseconds();
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

    private static int CountLines(string content)
    {
        if (content.Length == 0)
        {
            return 0;
        }

        var lineCount = 1;
        foreach (var character in content)
        {
            if (character == '\n')
            {
                lineCount++;
            }
        }

        return lineCount;
    }

    private static string SliceLines(string content, int startLine, int limit)
    {
        if (content.Length == 0)
        {
            return string.Empty;
        }

        var lines = content.Split('\n', StringSplitOptions.None);
        var startIndex = Math.Max(0, startLine - 1);
        if (startIndex >= lines.Length)
        {
            return string.Empty;
        }

        var endExclusive = Math.Min(lines.Length, startIndex + limit);
        return string.Join('\n', lines[startIndex..endExclusive]);
    }
}
