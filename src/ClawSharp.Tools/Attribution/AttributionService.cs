using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using ClawSharp.Core;

namespace ClawSharp.Tools;

public sealed class AttributionService
{
    private const string ProductUrl = "https://github.com/claw-sharp";
    private const string ClawSharpCommitAttribution = "Co-authored-by: ClawSharp <clawsharp@oneway8x.com>";
    private static readonly string[] InternalModelRepos =
    [
        "github.com:anthropics/claude-cli-internal",
        "github.com/anthropics/claude-cli-internal",
        "github.com:anthropics/anthropic",
        "github.com/anthropics/anthropic",
        "github.com:anthropics/apps",
        "github.com/anthropics/apps",
        "github.com:anthropics/casino",
        "github.com/anthropics/casino",
        "github.com:anthropics/dbt",
        "github.com/anthropics/dbt",
        "github.com:anthropics/dotfiles",
        "github.com/anthropics/dotfiles",
        "github.com:anthropics/terraform-config",
        "github.com/anthropics/terraform-config",
        "github.com:anthropics/hex-export",
        "github.com/anthropics/hex-export",
        "github.com:anthropics/feedback-v2",
        "github.com/anthropics/feedback-v2",
        "github.com:anthropics/labs",
        "github.com/anthropics/labs",
        "github.com:anthropics/argo-rollouts",
        "github.com/anthropics/argo-rollouts",
        "github.com:anthropics/starling-configs",
        "github.com/anthropics/starling-configs",
        "github.com:anthropics/ts-tools",
        "github.com/anthropics/ts-tools",
        "github.com:anthropics/ts-capsules",
        "github.com/anthropics/ts-capsules",
        "github.com:anthropics/feldspar-testing",
        "github.com/anthropics/feldspar-testing",
        "github.com:anthropics/trellis",
        "github.com/anthropics/trellis",
        "github.com:anthropics/claude-for-hiring",
        "github.com/anthropics/claude-for-hiring",
        "github.com:anthropics/forge-web",
        "github.com/anthropics/forge-web",
        "github.com:anthropics/infra-manifests",
        "github.com/anthropics/infra-manifests",
        "github.com:anthropics/mycro_manifests",
        "github.com/anthropics/mycro_manifests",
        "github.com:anthropics/mycro_configs",
        "github.com/anthropics/mycro_configs",
        "github.com:anthropics/mobile-apps",
        "github.com/anthropics/mobile-apps"
    ];

    private string? _repoClassCache; // "internal", "external", "none"

    public AttributionTexts GetAttributionTexts(ClawSharpSettings settings, string? remoteUrl = null)
    {
        // Internal check
        var isInternal = false;
        if (!string.IsNullOrWhiteSpace(remoteUrl))
        {
            isInternal = InternalModelRepos.Any(repo => remoteUrl.Contains(repo));
        }

        var defaultAttribution = $"🤖 Generated with [ClawSharp]({ProductUrl})";
        var defaultCommit = ClawSharpCommitAttribution;

        if (settings.Attribution is not null)
        {
            return new AttributionTexts(
                settings.Attribution.Commit ?? defaultCommit,
                settings.Attribution.Pr ?? defaultAttribution);
        }

        return new AttributionTexts(defaultCommit, defaultAttribution);
    }

    public AttributionState TrackFileModification(
        AttributionState state,
        string projectDirectory,
        string filePath,
        string oldContent,
        string newContent,
        double? mtime = null)
    {
        var normalizedPath = NormalizeFilePath(projectDirectory, filePath);
        var newFileState = ComputeFileModificationState(
            state.FileStates,
            projectDirectory,
            filePath,
            oldContent,
            newContent,
            mtime ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        if (newFileState is null)
        {
            return state;
        }

        var newFileStates = new Dictionary<string, FileAttributionState>(state.FileStates, StringComparer.Ordinal);
        newFileStates[normalizedPath] = newFileState;

        return state with
        {
            FileStates = newFileStates.ToImmutableDictionary(StringComparer.Ordinal)
        };
    }

    private FileAttributionState? ComputeFileModificationState(
        IReadOnlyDictionary<string, FileAttributionState> existingFileStates,
        string projectDirectory,
        string filePath,
        string oldContent,
        string newContent,
        double mtime)
    {
        var normalizedPath = NormalizeFilePath(projectDirectory, filePath);

        try
        {
            int claudeContribution;

            if (string.IsNullOrEmpty(oldContent) || string.IsNullOrEmpty(newContent))
            {
                claudeContribution = string.IsNullOrEmpty(oldContent) ? newContent.Length : oldContent.Length;
            }
            else
            {
                var minLen = Math.Min(oldContent.Length, newContent.Length);
                var prefixEnd = 0;
                while (prefixEnd < minLen && oldContent[prefixEnd] == newContent[prefixEnd])
                {
                    prefixEnd++;
                }

                var suffixLen = 0;
                while (suffixLen < minLen - prefixEnd &&
                       oldContent[oldContent.Length - 1 - suffixLen] == newContent[newContent.Length - 1 - suffixLen])
                {
                    suffixLen++;
                }

                var oldChangedLen = oldContent.Length - prefixEnd - suffixLen;
                var newChangedLen = newContent.Length - prefixEnd - suffixLen;
                claudeContribution = Math.Max(oldChangedLen, newChangedLen);
            }

            existingFileStates.TryGetValue(normalizedPath, out var existingState);
            var existingContribution = existingState?.ClaudeContribution ?? 0;

            return new FileAttributionState(
                ComputeContentHash(newContent),
                existingContribution + claudeContribution,
                mtime);
        }
        catch
        {
            return null;
        }
    }

    public async Task<AttributionData> CalculateCommitAttributionAsync(
        string projectDirectory,
        AttributionState state,
        IReadOnlyList<string> stagedFiles,
        CancellationToken cancellationToken = default)
    {
        var files = new Dictionary<string, FileAttribution>(StringComparer.Ordinal);
        var totalClaudeChars = 0;
        var totalHumanChars = 0;

        foreach (var filePath in stagedFiles)
        {
            var fullPath = Path.Combine(projectDirectory, filePath);
            if (!File.Exists(fullPath)) continue;

            var fileContent = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var normalizedPath = NormalizeFilePath(projectDirectory, filePath);
            state.FileStates.TryGetValue(normalizedPath, out var fileState);

            var claudeChars = fileState?.ClaudeContribution ?? 0;
            var humanChars = Math.Max(0, fileContent.Length - claudeChars);

            files[filePath] = new FileAttribution(
                claudeChars,
                humanChars,
                CalculatePercent(claudeChars, fileContent.Length),
                state.Surface);

            totalClaudeChars += claudeChars;
            totalHumanChars += humanChars;
        }

        var totalChars = totalClaudeChars + totalHumanChars;
        var claudePercent = CalculatePercent(totalClaudeChars, totalChars);

        var surfaceBreakdown = new Dictionary<string, SurfaceBreakdownEntry>(StringComparer.Ordinal)
        {
            [state.Surface] = new SurfaceBreakdownEntry(totalClaudeChars, claudePercent)
        };

        return new AttributionData(
            Version: 1,
            Summary: new AttributionSummary(
                claudePercent,
                totalClaudeChars,
                totalHumanChars,
                [state.Surface]),
            Files: files.ToImmutableDictionary(StringComparer.Ordinal),
            SurfaceBreakdown: surfaceBreakdown.ToImmutableDictionary(StringComparer.Ordinal),
            ExcludedGenerated: [],
            Sessions: []);
    }

    public async Task<string> GetEnhancedPRAttributionAsync(
        ClawSharpSettings settings,
        AttributionState state,
        string? remoteUrl = null,
        int promptCount = 0,
        int memoryAccessCount = 0,
        CancellationToken cancellationToken = default)
    {
        var isInternal = false;
        if (!string.IsNullOrWhiteSpace(remoteUrl))
        {
            isInternal = InternalModelRepos.Any(repo => remoteUrl.Contains(repo));
        }

        var model = MainLoopModelResolver.Resolve(settings.Runtime.Model);
        // In C# we use the resolved model directly.
        // Parity: getPublicModelName(model) logic
        var shortModelName = isInternal ? model : SanitizeModelName(model);

        var defaultAttribution = $"🤖 Generated with [ClawSharp]({ProductUrl})";

        // If user has custom PR attribution, use that
        if (settings.Attribution?.Pr is not null)
        {
            return settings.Attribution.Pr;
        }

        // Forward compatibility: check for attribution data
        var claudePercent = 0;
        // In a real implementation we would call CalculateCommitAttributionAsync for all changed files
        // but for now we follow the logic provided by state.
        // Simplified for parity with what's available in state
        if (state.FileStates.Count > 0)
        {
            var totalClaudeChars = state.FileStates.Values.Sum(fs => fs.ClaudeContribution);
            // We don't have the current file content here, so we can't easily calculate total chars.
            // Simplified: if we have any Claude contribution, we'll use a placeholder or 0 if unknown.
        }

        if (claudePercent == 0 && promptCount == 0 && memoryAccessCount == 0)
        {
            return defaultAttribution;
        }

        var memSuffix = memoryAccessCount > 0
            ? $", {memoryAccessCount} {(memoryAccessCount == 1 ? "memory" : "memories")} recalled"
            : "";

        return $"🤖 Generated with [ClawSharp]({ProductUrl}) ({claudePercent}% {promptCount}-shotted by {shortModelName}{memSuffix})";
    }

    private static int CalculatePercent(int part, int total)
    {
        if (total == 0) return 0;
        return (int)Math.Round((double)part / total * 100);
    }

    private string ComputeContentHash(string content)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(hash);
    }

    private string NormalizeFilePath(string projectDirectory, string filePath)
    {
        if (!Path.IsPathRooted(filePath))
        {
            return filePath.Replace('\\', '/');
        }

        var absoluteProjectDirectory = Path.GetFullPath(projectDirectory);
        var absolutePath = Path.GetFullPath(filePath);

        if (absolutePath.StartsWith(absoluteProjectDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetRelativePath(absoluteProjectDirectory, absolutePath).Replace('\\', '/');
        }

        return absolutePath.Replace('\\', '/');
    }

    private string SanitizeModelName(string modelName)
    {
        if (modelName.Contains("opus-4-6")) return "claude-opus-4-6";
        if (modelName.Contains("opus-4-5")) return "claude-opus-4-5";
        if (modelName.Contains("opus-4-1")) return "claude-opus-4-1";
        if (modelName.Contains("opus-4")) return "claude-opus-4";
        if (modelName.Contains("sonnet-4-6")) return "claude-sonnet-4-6";
        if (modelName.Contains("sonnet-4-5")) return "claude-sonnet-4-5";
        if (modelName.Contains("sonnet-4")) return "claude-sonnet-4";
        if (modelName.Contains("sonnet-3-7")) return "claude-sonnet-3-7";
        if (modelName.Contains("haiku-4-5")) return "claude-haiku-4-5";
        if (modelName.Contains("haiku-3-5")) return "claude-haiku-3-5";
        return "claude";
    }
}

public sealed record AttributionTexts(
    string Commit,
    string Pr);
