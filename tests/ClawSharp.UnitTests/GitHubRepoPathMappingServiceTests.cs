// TS origin: ./utils/githubRepoPathMapping.ts, ./utils/detectRepository.ts
using System.Text;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class GitHubRepoPathMappingServiceTests
{
    [Fact]
    public async Task UpdateAsync_Promotes_Current_Clone_To_Front_Of_Global_Config()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "clawsharp-repo-paths", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(tempDirectory, "workspace");
        var previousClone = Path.Combine(tempDirectory, "previous");
        var globalConfigPath = Path.Combine(tempDirectory, ".claude.json");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(previousClone);
        await File.WriteAllTextAsync(
            globalConfigPath,
            """
            {
              "githubRepoPaths": {
                "owner/repo": [
                  "REPLACE_ME"
                ]
              }
            }
            """.Replace("REPLACE_ME", EscapeJson(previousClone), StringComparison.Ordinal),
            Encoding.UTF8);

        try
        {
            var service = new GitHubRepoPathMappingService(
                globalConfigPath,
                static (fileName, arguments, workingDirectory, _) =>
                {
                    if (arguments.SequenceEqual(["config", "--get", "remote.origin.url"]))
                    {
                        return Task.FromResult(new ProcessExecutionResult(0, "https://github.com/owner/repo.git", string.Empty));
                    }

                    if (arguments.SequenceEqual(["rev-parse", "--show-toplevel"]))
                    {
                        return Task.FromResult(new ProcessExecutionResult(0, workingDirectory ?? string.Empty, string.Empty));
                    }

                    return Task.FromResult(new ProcessExecutionResult(1, string.Empty, "unexpected command"));
                },
                NormalizePath);

            await service.UpdateAsync(workspaceRoot);

            var knownPaths = service.GetKnownPathsForRepo("owner/repo");
            Assert.Equal(NormalizePath(workspaceRoot), knownPaths[0]);
            Assert.Equal(NormalizePath(previousClone), knownPaths[1]);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UpdateAsync_Stores_Canonicalized_Git_Root_Path()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "clawsharp-repo-paths", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(tempDirectory, "symlink-root");
        var canonicalRoot = Path.Combine(tempDirectory, "actual-root");
        var globalConfigPath = Path.Combine(tempDirectory, ".claude.json");
        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(canonicalRoot);

        try
        {
            var service = new GitHubRepoPathMappingService(
                globalConfigPath,
                static (_, arguments, workingDirectory, _) =>
                {
                    if (arguments.SequenceEqual(["config", "--get", "remote.origin.url"]))
                    {
                        return Task.FromResult(new ProcessExecutionResult(0, "https://github.com/owner/repo.git", string.Empty));
                    }

                    if (arguments.SequenceEqual(["rev-parse", "--show-toplevel"]))
                    {
                        return Task.FromResult(new ProcessExecutionResult(0, workingDirectory ?? string.Empty, string.Empty));
                    }

                    return Task.FromResult(new ProcessExecutionResult(1, string.Empty, "unexpected command"));
                },
                path => string.Equals(path, workspaceRoot, StringComparison.OrdinalIgnoreCase)
                    ? NormalizePath(canonicalRoot)
                    : NormalizePath(path));

            await service.UpdateAsync(workspaceRoot);

            var knownPaths = service.GetKnownPathsForRepo("owner/repo");
            Assert.Equal(NormalizePath(canonicalRoot), knownPaths[0]);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task UpdateAsync_Falls_Back_To_Base_Path_When_Canonicalization_Throws()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "clawsharp-repo-paths", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(tempDirectory, "workspace");
        var globalConfigPath = Path.Combine(tempDirectory, ".claude.json");
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var service = new GitHubRepoPathMappingService(
                globalConfigPath,
                static (_, arguments, workingDirectory, _) =>
                {
                    if (arguments.SequenceEqual(["config", "--get", "remote.origin.url"]))
                    {
                        return Task.FromResult(new ProcessExecutionResult(0, "https://github.com/owner/repo.git", string.Empty));
                    }

                    if (arguments.SequenceEqual(["rev-parse", "--show-toplevel"]))
                    {
                        return Task.FromResult(new ProcessExecutionResult(0, workingDirectory ?? string.Empty, string.Empty));
                    }

                    return Task.FromResult(new ProcessExecutionResult(1, string.Empty, "unexpected command"));
                },
                _ => throw new IOException("canonicalization failed"));

            await service.UpdateAsync(workspaceRoot);

            var knownPaths = service.GetKnownPathsForRepo("owner/repo");
            Assert.Equal(workspaceRoot, knownPaths[0]);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ResolveRepoAsync_Returns_First_Existing_Tracked_Path()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "clawsharp-repo-paths", Guid.NewGuid().ToString("N"));
        var existingPath = Path.Combine(tempDirectory, "repo");
        var missingPath = Path.Combine(tempDirectory, "missing");
        var globalConfigPath = Path.Combine(tempDirectory, ".claude.json");
        Directory.CreateDirectory(existingPath);
        await File.WriteAllTextAsync(
            globalConfigPath,
            """
            {
              "githubRepoPaths": {
                "owner/repo": [
                  "MISSING",
                  "EXISTING"
                ]
              }
            }
            """
                .Replace("MISSING", EscapeJson(missingPath), StringComparison.Ordinal)
                .Replace("EXISTING", EscapeJson(existingPath), StringComparison.Ordinal),
            Encoding.UTF8);

        try
        {
            var service = new GitHubRepoPathMappingService(globalConfigPath);

            var resolved = await service.ResolveRepoAsync("owner/repo");

            Assert.Equal(existingPath, resolved);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("https://github.com/owner/repo.git", "owner/repo")]
    [InlineData("git@github.com:owner/repo.git", "owner/repo")]
    [InlineData("owner/repo", "owner/repo")]
    [InlineData("https://ghe.example.com/owner/repo.git", null)]
    public void ParseGitHubRepository_Matches_TypeScript_Formats(string input, string? expected)
    {
        Assert.Equal(expected, GitHubRepoPathMappingService.ParseGitHubRepository(input));
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).Normalize(NormalizationForm.FormC);
    }

    private static string EscapeJson(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal);
    }
}
