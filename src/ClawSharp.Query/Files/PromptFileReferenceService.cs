using System.Text;
using System.Text.RegularExpressions;
using ClawSharp.Core;

namespace ClawSharp.Query.Files;

internal sealed class PromptFileReferenceService
{
    private const int MaxBytesToAttach = 128 * 1024;
    private const int MaxCharactersToAttach = 16_000;
    private const int MaxLinesToAttach = 400;

    private static readonly Regex FileReferencePattern = new(
        @"(?<![\w@])@(?<path>[A-Za-z0-9_./-]+)",
        RegexOptions.Compiled);

    public Task<QueryTurnRequest> ExpandPromptFileReferencesAsync(
        QueryTurnRequest request,
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || string.IsNullOrWhiteSpace(request.EffectiveUserInput))
        {
            return Task.FromResult(request);
        }

        var referencedPaths = FileReferencePattern.Matches(request.EffectiveUserInput)
            .Select(static match => match.Groups["path"].Value)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (referencedPaths.Length == 0)
        {
            return Task.FromResult(request);
        }

        var modelTurnContext = request.ModelTurnContext ?? QueryModelTurnContext.ReplMainThread;
        var userContext = new Dictionary<string, string>(modelTurnContext.UserContext, StringComparer.Ordinal);
        var attachedCount = 0;

        foreach (var referencedPath in referencedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var resolvedFile = ResolveFilePath(workspaceRoot, referencedPath);
            if (resolvedFile is null || !File.Exists(resolvedFile))
            {
                continue;
            }

            FileInfo fileInfo;
            try
            {
                fileInfo = new FileInfo(resolvedFile);
            }
            catch
            {
                continue;
            }

            if (fileInfo.Length > MaxBytesToAttach)
            {
                continue;
            }

            FileTextMetadata metadata;
            try
            {
                metadata = FileTextOperations.ReadFileWithMetadata(resolvedFile);
            }
            catch
            {
                continue;
            }

            if (metadata.Content.IndexOf('\0') >= 0)
            {
                continue;
            }

            var (content, wasTruncated) = TruncateContent(metadata.Content);
            var relativePath = Path.GetRelativePath(workspaceRoot, resolvedFile).Replace('\\', '/');
            userContext[$"Attached file @{relativePath}"] = BuildAttachmentContext(relativePath, resolvedFile, content, wasTruncated);
            attachedCount += 1;
        }

        if (attachedCount == 0)
        {
            return Task.FromResult(request);
        }

        return Task.FromResult(request with
        {
            ModelTurnContext = new QueryModelTurnContext(
                modelTurnContext.SystemPrompt,
                userContext,
                modelTurnContext.SystemContext,
                modelTurnContext.QuerySource)
        });
    }

    private static string? ResolveFilePath(string workspaceRoot, string referencedPath)
    {
        var trimmedPath = referencedPath.Trim();
        if (trimmedPath.Length == 0)
        {
            return null;
        }

        var combinedPath = Path.GetFullPath(Path.Combine(workspaceRoot, trimmedPath));
        var normalizedWorkspaceRoot = EnsureTrailingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        if (!combinedPath.StartsWith(normalizedWorkspaceRoot, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(combinedPath, normalizedWorkspaceRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return combinedPath;
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
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

    private static string BuildAttachmentContext(string relativePath, string absolutePath, string content, bool wasTruncated)
    {
        var builder = new StringBuilder();
        builder.Append("Relative path: ");
        builder.AppendLine(relativePath);
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
}
