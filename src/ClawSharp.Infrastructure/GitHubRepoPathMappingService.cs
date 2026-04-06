// TS origin: ./utils/githubRepoPathMapping.ts, ./utils/detectRepository.ts
using System.Text;
using System.Text.Json;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class GitHubRepoPathMappingService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _globalConfigPath;
    private readonly Func<string, IReadOnlyList<string>, string?, CancellationToken, Task<ProcessExecutionResult>> _executeAsync;
    private readonly Func<string, string> _normalizePath;

    public GitHubRepoPathMappingService(
        string? globalConfigPath = null,
        Func<string, IReadOnlyList<string>, string?, CancellationToken, Task<ProcessExecutionResult>>? executeAsync = null,
        Func<string, string>? normalizePath = null)
    {
        _globalConfigPath = globalConfigPath ?? ClaudeConfigPaths.GetGlobalClaudeFilePath();
        _executeAsync = executeAsync ?? ((fileName, args, workingDirectory, token) =>
            ProcessExecutionUtilities.ExecuteAsync(fileName, args, workingDirectory, cancellationToken: token));
        _normalizePath = normalizePath ?? NormalizePath;
    }

    public async Task UpdateAsync(string workspaceRoot, CancellationToken cancellationToken = default)
    {
        try
        {
            var repo = await DetectCurrentRepositoryAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(repo))
            {
                return;
            }

            var gitRoot = await ResolveGitRootAsync(workspaceRoot, cancellationToken).ConfigureAwait(false) ?? workspaceRoot;
            var currentPath = NormalizePathOrFallback(gitRoot);
            var repoKey = repo.ToLowerInvariant();
            var config = Load();
            config.GitHubRepoPaths.TryGetValue(repoKey, out var existingPaths);
            existingPaths ??= [];

            if (existingPaths.Count > 0 && GetPathComparer().Equals(existingPaths[0], currentPath))
            {
                return;
            }

            var updatedPaths = new List<string> { currentPath };
            foreach (var path in existingPaths)
            {
                if (!GetPathComparer().Equals(path, currentPath))
                {
                    updatedPaths.Add(path);
                }
            }

            config.GitHubRepoPaths[repoKey] = updatedPaths;
            Save(config);
        }
        catch
        {
            // TS parity: repo path mapping is non-blocking startup housekeeping.
        }
    }

    public IReadOnlyList<string> GetKnownPathsForRepo(string repo)
    {
        var config = Load();
        var repoKey = repo.ToLowerInvariant();
        return config.GitHubRepoPaths.TryGetValue(repoKey, out var paths)
            ? paths
            : [];
    }

    public async Task<string?> ResolveRepoAsync(string repo, CancellationToken cancellationToken = default)
    {
        foreach (var path in GetKnownPathsForRepo(repo))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    public static string? ParseGitHubRepository(string input)
    {
        var trimmed = input.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        var parsed = ParseGitRemote(trimmed);
        if (parsed is not null)
        {
            return string.Equals(parsed.Value.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                ? $"{parsed.Value.Owner}/{parsed.Value.Name}"
                : null;
        }

        if (!trimmed.Contains("://", StringComparison.Ordinal) &&
            !trimmed.Contains('@', StringComparison.Ordinal) &&
            trimmed.Contains('/', StringComparison.Ordinal))
        {
            var parts = trimmed.Split('/');
            if (parts.Length == 2 &&
                !string.IsNullOrWhiteSpace(parts[0]) &&
                !string.IsNullOrWhiteSpace(parts[1]))
            {
                var repo = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
                    ? parts[1][..^4]
                    : parts[1];
                return $"{parts[0]}/{repo}";
            }
        }

        return null;
    }

    private async Task<string?> DetectCurrentRepositoryAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var result = await _executeAsync(
            "git",
            ["config", "--get", "remote.origin.url"],
            workspaceRoot,
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
        {
            return null;
        }

        return ParseGitHubRepository(result.Stdout);
    }

    private async Task<string?> ResolveGitRootAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var result = await _executeAsync(
            "git",
            ["rev-parse", "--show-toplevel"],
            workspaceRoot,
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Stdout))
        {
            return null;
        }

        return result.Stdout.Trim();
    }

    private RepoPathConfig Load()
    {
        if (!File.Exists(_globalConfigPath))
        {
            return new RepoPathConfig();
        }

        try
        {
            return JsonSerializer.Deserialize<RepoPathConfig>(
                       File.ReadAllText(_globalConfigPath),
                       SerializerOptions) ??
                   new RepoPathConfig();
        }
        catch
        {
            return new RepoPathConfig();
        }
    }

    private void Save(RepoPathConfig config)
    {
        var directory = Path.GetDirectoryName(_globalConfigPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_globalConfigPath, JsonSerializer.Serialize(config, SerializerOptions), Encoding.UTF8);
    }

    private static string NormalizePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return PathUtilities.ResolveRealPathLikeNode(path).Normalize(NormalizationForm.FormC);
    }

    private string NormalizePathOrFallback(string path)
    {
        try
        {
            return _normalizePath(path);
        }
        catch
        {
            return path;
        }
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    private static (string Host, string Owner, string Name)? ParseGitRemote(string input)
    {
        var trimmed = input.Trim();
        var sshMatch = System.Text.RegularExpressions.Regex.Match(
            trimmed,
            @"^git@([^:]+):([^/]+)/([^/]+?)(?:\.git)?$");
        if (sshMatch.Success)
        {
            var host = sshMatch.Groups[1].Value;
            if (!LooksLikeRealHostname(host))
            {
                return null;
            }

            return (host, sshMatch.Groups[2].Value, sshMatch.Groups[3].Value);
        }

        var urlMatch = System.Text.RegularExpressions.Regex.Match(
            trimmed,
            @"^(https?|ssh|git):\/\/(?:[^@]+@)?([^/:]+(?::\d+)?)\/([^/]+)\/([^/]+?)(?:\.git)?$");
        if (!urlMatch.Success)
        {
            return null;
        }

        var protocol = urlMatch.Groups[1].Value;
        var hostWithPort = urlMatch.Groups[2].Value;
        var hostWithoutPort = hostWithPort.Split(':', 2)[0];
        if (!LooksLikeRealHostname(hostWithoutPort))
        {
            return null;
        }

        var resolvedHost = string.Equals(protocol, "http", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(protocol, "https", StringComparison.OrdinalIgnoreCase)
            ? hostWithPort
            : hostWithoutPort;
        return (resolvedHost, urlMatch.Groups[3].Value, urlMatch.Groups[4].Value);
    }

    private static bool LooksLikeRealHostname(string host)
    {
        if (!host.Contains('.', StringComparison.Ordinal))
        {
            return false;
        }

        var lastSegment = host.Split('.').LastOrDefault();
        if (string.IsNullOrWhiteSpace(lastSegment))
        {
            return false;
        }

        return lastSegment.All(static character => char.IsLetter(character));
    }

    private sealed class RepoPathConfig
    {
        public Dictionary<string, List<string>> GitHubRepoPaths { get; init; } = new(StringComparer.Ordinal);
    }
}
