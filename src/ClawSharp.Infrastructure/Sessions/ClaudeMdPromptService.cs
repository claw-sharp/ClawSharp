using YamlDotNet.Serialization;

namespace ClawSharp.Infrastructure;

public sealed class ClaudeMdPromptService
{
    private const string InstructionPrompt =
        "Codebase and user instructions are shown below. Be sure to adhere to these instructions. IMPORTANT: These instructions OVERRIDE any default behavior and you MUST follow them exactly as written.";

    private readonly IDeserializer _yamlDeserializer;
    private readonly Func<string, CancellationToken, Task<WorkspaceSearchPaths>> _searchPathResolver;

    public ClaudeMdPromptService(
        IDeserializer? yamlDeserializer = null,
        Func<string, CancellationToken, Task<WorkspaceSearchPaths>>? searchPathResolver = null)
    {
        _yamlDeserializer = yamlDeserializer ?? new DeserializerBuilder().Build();
        _searchPathResolver = searchPathResolver ?? ((workspaceRoot, cancellationToken) =>
            WorkspaceSearchPathResolver.ResolveAsync(workspaceRoot, cancellationToken: cancellationToken));
    }

    public async Task<string?> LoadPromptAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        var searchPaths = await _searchPathResolver(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var files = await DiscoverInstructionFilesAsync(searchPaths, cancellationToken).ConfigureAwait(false);
        if (files.Count == 0)
        {
            return null;
        }

        var sections = files
            .Select(static file => $"Contents of {file.Path}{GetDescription(file.Type)}:\n\n{file.Content}")
            .ToArray();
        return $"{InstructionPrompt}\n\n{string.Join("\n\n", sections)}";
    }

    private async Task<IReadOnlyList<ClaudeInstructionFile>> DiscoverInstructionFilesAsync(
        WorkspaceSearchPaths searchPaths,
        CancellationToken cancellationToken)
    {
        var files = new List<ClaudeInstructionFile>();
        var seenPaths = new HashSet<string>(GetPathComparer());

        foreach (var directory in searchPaths.AncestorDirectories.Reverse())
        {
            cancellationToken.ThrowIfCancellationRequested();

            await TryAddInstructionFileAsync(
                    files,
                    seenPaths,
                    Path.Combine(directory, "CLAUDE.md"),
                    ClaudeInstructionType.Project,
                    isRuleFile: false,
                    cancellationToken)
                .ConfigureAwait(false);
            await TryAddInstructionFileAsync(
                    files,
                    seenPaths,
                    Path.Combine(directory, ".claude", "CLAUDE.md"),
                    ClaudeInstructionType.Project,
                    isRuleFile: false,
                    cancellationToken)
                .ConfigureAwait(false);
            await AddRuleFilesAsync(
                    files,
                    seenPaths,
                    Path.Combine(directory, ".claude", "rules"),
                    cancellationToken)
                .ConfigureAwait(false);
            await TryAddInstructionFileAsync(
                    files,
                    seenPaths,
                    Path.Combine(directory, "CLAUDE.local.md"),
                    ClaudeInstructionType.Local,
                    isRuleFile: false,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return files;
    }

    private async Task AddRuleFilesAsync(
        ICollection<ClaudeInstructionFile> files,
        ISet<string> seenPaths,
        string rulesDirectory,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(rulesDirectory))
        {
            return;
        }

        foreach (var ruleFile in Directory
                     .EnumerateFiles(rulesDirectory, "*.md", SearchOption.AllDirectories)
                     .OrderBy(static path => path, GetPathComparer()))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await TryAddInstructionFileAsync(
                    files,
                    seenPaths,
                    ruleFile,
                    ClaudeInstructionType.Project,
                    isRuleFile: true,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task TryAddInstructionFileAsync(
        ICollection<ClaudeInstructionFile> files,
        ISet<string> seenPaths,
        string path,
        ClaudeInstructionType type,
        bool isRuleFile,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        if (!seenPaths.Add(fullPath))
        {
            return;
        }

        string rawContent;
        try
        {
            rawContent = await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        var (frontmatter, content) = SplitFrontmatter(rawContent);
        if (isRuleFile && HasConditionalPaths(frontmatter))
        {
            return;
        }

        var trimmedContent = content.Trim();
        if (string.IsNullOrWhiteSpace(trimmedContent))
        {
            return;
        }

        files.Add(new ClaudeInstructionFile(fullPath, type, trimmedContent));
    }

    private (Dictionary<string, object?>? Frontmatter, string Content) SplitFrontmatter(string text)
    {
        if (!text.StartsWith("---", StringComparison.Ordinal))
        {
            return (null, text);
        }

        using var reader = new StringReader(text);
        if (!string.Equals(reader.ReadLine(), "---", StringComparison.Ordinal))
        {
            return (null, text);
        }

        var frontmatterBuilder = new System.Text.StringBuilder();
        string? line;
        var foundClosing = false;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.Equals(line, "---", StringComparison.Ordinal))
            {
                foundClosing = true;
                break;
            }

            frontmatterBuilder.AppendLine(line);
        }

        if (!foundClosing)
        {
            return (null, text);
        }

        try
        {
            var parsed = _yamlDeserializer.Deserialize<Dictionary<object, object?>>(frontmatterBuilder.ToString()) ?? [];
            var normalized = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var pair in parsed)
            {
                if (pair.Key is null)
                {
                    continue;
                }

                normalized[pair.Key.ToString() ?? string.Empty] = pair.Value;
            }

            return (normalized, reader.ReadToEnd());
        }
        catch
        {
            return (null, text);
        }
    }

    private static bool HasConditionalPaths(IReadOnlyDictionary<string, object?>? frontmatter)
    {
        if (frontmatter is null || !frontmatter.TryGetValue("paths", out var value) || value is null)
        {
            return false;
        }

        return value switch
        {
            string text => !string.IsNullOrWhiteSpace(text),
            IEnumerable<object?> sequence => sequence.Any(static item => item is not null && !string.IsNullOrWhiteSpace(item.ToString())),
            _ => true
        };
    }

    private static string GetDescription(ClaudeInstructionType type)
    {
        return type switch
        {
            ClaudeInstructionType.Project => " (project instructions, checked into the codebase)",
            ClaudeInstructionType.Local => " (user's private project instructions, not checked in)",
            _ => string.Empty
        };
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    private enum ClaudeInstructionType
    {
        Project,
        Local
    }

    private sealed record ClaudeInstructionFile(
        string Path,
        ClaudeInstructionType Type,
        string Content);
}
