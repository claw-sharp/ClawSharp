using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ClawSharp.Core;

namespace ClawSharp.Query.Attachments;

internal sealed partial class PromptAttachmentReferenceService
{
    private const int MaxBytesToAttachAsText = 128 * 1024;
    private const int MaxCharactersToAttach = 16_000;
    private const int MaxLinesToAttach = 400;
    private const long MaxImageBytes = (5 * 1024 * 1024 * 3) / 4;
    private static readonly HashSet<string> PdfExtensions = [".pdf"];

    [GeneratedRegex(@"<clawsharp-attachment>(?<json>.*?)</clawsharp-attachment>", RegexOptions.Singleline)]
    private static partial Regex AttachmentMarkerPattern();

    public QueryTurnRequest ExtractPromptAttachments(QueryTurnRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserInput))
        {
            return request;
        }

        var matches = AttachmentMarkerPattern().Matches(request.UserInput);
        if (matches.Count == 0)
        {
            return request;
        }

        List<QueryPromptAttachment> attachments = [];
        foreach (Match match in matches)
        {
            var serialized = match.Groups["json"].Value;
            if (string.IsNullOrWhiteSpace(serialized))
            {
                continue;
            }

            try
            {
                if (JsonNode.Parse(serialized) is not JsonObject parsed ||
                    string.IsNullOrWhiteSpace(parsed["path"]?.GetValue<string>()) ||
                    string.IsNullOrWhiteSpace(parsed["name"]?.GetValue<string>()))
                {
                    continue;
                }

                if (!TryParseKind(parsed["kind"]?.GetValue<string>(), out var kind))
                {
                    continue;
                }

                attachments.Add(new QueryPromptAttachment(
                    kind,
                    parsed["path"]!.GetValue<string>().Trim(),
                    parsed["name"]!.GetValue<string>().Trim()));
            }
            catch
            {
                continue;
            }
        }

        var resolvedUserInput = AttachmentMarkerPattern().Replace(request.UserInput, string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim();

        return request with
        {
            ResolvedUserInput = resolvedUserInput,
            PromptAttachments = attachments
        };
    }

    public Task<QueryTurnRequest> ExpandPromptAttachmentsAsync(
        QueryTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        var attachments = request.EffectivePromptAttachments;
        if (attachments.Count == 0)
        {
            return Task.FromResult(request);
        }

        var modelTurnContext = request.ModelTurnContext ?? QueryModelTurnContext.ReplMainThread;
        var userContext = new Dictionary<string, string>(modelTurnContext.UserContext, StringComparer.Ordinal);
        List<QueryPromptAttachment> hydratedAttachments = [];

        foreach (var attachment in attachments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (attachment.Kind == QueryPromptAttachmentKind.Image)
            {
                if (TryReadImageAttachment(attachment, out var hydratedImage))
                {
                    hydratedAttachments.Add(hydratedImage);
                    userContext[$"Attached image {hydratedImage.Name}"] = BuildImageAttachmentContext(hydratedImage);
                }

                continue;
            }

            if (TryReadTextAttachment(attachment, out var fileContext))
            {
                hydratedAttachments.Add(attachment);
                userContext[$"Attached file {attachment.Name}"] = fileContext;
                continue;
            }

            if (TryBuildReferencedFileAttachmentContext(
                    attachment,
                    out var referencedAttachment,
                    out var referencedFileContext))
            {
                hydratedAttachments.Add(referencedAttachment);
                userContext[$"Attached file {referencedAttachment.Name}"] = referencedFileContext;
            }
        }

        return Task.FromResult(request with
        {
            ModelTurnContext = new QueryModelTurnContext(
                modelTurnContext.SystemPrompt,
                userContext,
                modelTurnContext.SystemContext,
                modelTurnContext.QuerySource),
            PromptAttachments = hydratedAttachments
        });
    }

    private static bool TryReadTextAttachment(QueryPromptAttachment attachment, out string context)
    {
        context = string.Empty;
        var fullPath = ResolveAbsolutePath(attachment.Path);
        if (fullPath is null || !File.Exists(fullPath))
        {
            return false;
        }

        FileInfo fileInfo;
        try
        {
            fileInfo = new FileInfo(fullPath);
        }
        catch
        {
            return false;
        }

        if (fileInfo.Length > MaxBytesToAttachAsText)
        {
            return false;
        }

        FileTextMetadata metadata;
        try
        {
            metadata = FileTextOperations.ReadFileWithMetadata(fullPath);
        }
        catch
        {
            return false;
        }

        if (metadata.Content.IndexOf('\0') >= 0)
        {
            return false;
        }

        var (content, wasTruncated) = TruncateContent(metadata.Content);
        context = BuildFileAttachmentContext(attachment.Name, fullPath, content, wasTruncated);
        return true;
    }

    private static bool TryBuildReferencedFileAttachmentContext(
        QueryPromptAttachment attachment,
        out QueryPromptAttachment hydratedAttachment,
        out string context)
    {
        hydratedAttachment = attachment;
        context = string.Empty;

        var fullPath = ResolveAbsolutePath(attachment.Path);
        if (fullPath is null || !File.Exists(fullPath))
        {
            return false;
        }

        FileInfo fileInfo;
        try
        {
            fileInfo = new FileInfo(fullPath);
        }
        catch
        {
            return false;
        }

        hydratedAttachment = attachment with
        {
            Path = fullPath
        };

        context = IsPdfExtension(fullPath)
            ? BuildPdfAttachmentReferenceContext(attachment.Name, fullPath, fileInfo.Length)
            : BuildBinaryAttachmentReferenceContext(attachment.Name, fullPath, fileInfo.Length);
        return true;
    }

    private static bool TryReadImageAttachment(QueryPromptAttachment attachment, out QueryPromptAttachment hydratedAttachment)
    {
        hydratedAttachment = attachment;
        var fullPath = ResolveAbsolutePath(attachment.Path);
        if (fullPath is null || !File.Exists(fullPath) || !IsSupportedImageExtension(fullPath))
        {
            return false;
        }

        byte[] imageBytes;
        try
        {
            imageBytes = File.ReadAllBytes(fullPath);
        }
        catch
        {
            return false;
        }

        if (imageBytes.Length == 0 || imageBytes.LongLength > MaxImageBytes)
        {
            return false;
        }

        hydratedAttachment = attachment with
        {
            Path = fullPath,
            MediaType = DetectImageMediaType(imageBytes),
            Base64Data = Convert.ToBase64String(imageBytes)
        };
        return true;
    }

    private static string? ResolveAbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch
        {
            return null;
        }
    }

    private static (string Content, bool WasTruncated) TruncateContent(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var limitedLines = lines.Take(MaxLinesToAttach).ToArray();
        var joined = string.Join('\n', limitedLines);
        var wasTruncated = lines.Length > limitedLines.Length || joined.Length > MaxCharactersToAttach;
        if (joined.Length > MaxCharactersToAttach)
        {
            joined = joined[..MaxCharactersToAttach];
        }

        return (joined, wasTruncated);
    }

    private static string BuildFileAttachmentContext(string name, string absolutePath, string content, bool wasTruncated)
    {
        var builder = new StringBuilder();
        builder.Append("Name: ");
        builder.AppendLine(name);
        builder.Append("Absolute path: ");
        builder.AppendLine(absolutePath.Replace('\\', '/'));
        if (wasTruncated)
        {
            builder.AppendLine("Note: content was truncated to fit the current context window.");
        }

        builder.AppendLine();
        builder.AppendLine(content);
        return builder.ToString().TrimEnd();
    }

    private static string BuildPdfAttachmentReferenceContext(string name, string absolutePath, long sizeBytes)
    {
        var builder = new StringBuilder();
        builder.Append("Name: ");
        builder.AppendLine(name);
        builder.Append("Absolute path: ");
        builder.AppendLine(absolutePath.Replace('\\', '/'));
        builder.AppendLine("Type: PDF document");
        builder.Append("Size: ");
        builder.AppendLine(FormatFileSize(sizeBytes));
        builder.AppendLine("This PDF was attached for the current turn, but it is not inlined into the prompt as text.");
        builder.AppendLine("Use the Read tool with the exact absolute path above to inspect it.");
        builder.AppendLine("If the PDF is long, use the pages parameter to read a smaller page range first.");
        return builder.ToString().TrimEnd();
    }

    private static string BuildBinaryAttachmentReferenceContext(string name, string absolutePath, long sizeBytes)
    {
        var builder = new StringBuilder();
        builder.Append("Name: ");
        builder.AppendLine(name);
        builder.Append("Absolute path: ");
        builder.AppendLine(absolutePath.Replace('\\', '/'));
        builder.Append("Type: ");
        builder.AppendLine(GetFileTypeDescription(absolutePath));
        builder.Append("Size: ");
        builder.AppendLine(FormatFileSize(sizeBytes));
        builder.AppendLine("This file was attached for the current turn, but it is not inlined into the prompt as text.");
        builder.AppendLine("Use the Read tool with the exact absolute path above if you need to inspect it.");
        return builder.ToString().TrimEnd();
    }

    private static string BuildImageAttachmentContext(QueryPromptAttachment attachment)
    {
        var builder = new StringBuilder();
        builder.Append("Name: ");
        builder.AppendLine(attachment.Name);
        builder.Append("Absolute path: ");
        builder.AppendLine(attachment.Path.Replace('\\', '/'));
        builder.Append("Media type: ");
        builder.AppendLine(attachment.MediaType ?? "image/png");
        builder.AppendLine("The image is attached as native model input for this turn.");
        return builder.ToString().TrimEnd();
    }

    private static bool IsPdfExtension(string filePath)
    {
        return PdfExtensions.Contains(Path.GetExtension(filePath));
    }

    private static string GetFileTypeDescription(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "Attached file";
        }

        return $"{extension.ToLowerInvariant()} file";
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

    private static bool TryParseKind(string? rawKind, out QueryPromptAttachmentKind kind)
    {
        if (string.Equals(rawKind, "file", StringComparison.OrdinalIgnoreCase))
        {
            kind = QueryPromptAttachmentKind.File;
            return true;
        }

        if (string.Equals(rawKind, "image", StringComparison.OrdinalIgnoreCase))
        {
            kind = QueryPromptAttachmentKind.Image;
            return true;
        }

        kind = default;
        return false;
    }

    private static bool IsSupportedImageExtension(string filePath)
    {
        return SupportedImageExtensions.Contains(Path.GetExtension(filePath));
    }

    private static string DetectImageMediaType(byte[] buffer)
    {
        if (buffer.Length < 4)
        {
            return "image/png";
        }

        if (buffer[0] == 0x89 &&
            buffer[1] == 0x50 &&
            buffer[2] == 0x4E &&
            buffer[3] == 0x47)
        {
            return "image/png";
        }

        if (buffer[0] == 0xFF &&
            buffer[1] == 0xD8 &&
            buffer[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (buffer[0] == 0x47 &&
            buffer[1] == 0x49 &&
            buffer[2] == 0x46)
        {
            return "image/gif";
        }

        if (buffer.Length >= 12 &&
            buffer[0] == 0x52 &&
            buffer[1] == 0x49 &&
            buffer[2] == 0x46 &&
            buffer[3] == 0x46 &&
            buffer[8] == 0x57 &&
            buffer[9] == 0x45 &&
            buffer[10] == 0x42 &&
            buffer[11] == 0x50)
        {
            return "image/webp";
        }

        return "image/png";
    }

    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".gif",
        ".webp"
    };
}
