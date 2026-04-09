using System.Diagnostics;
using System.Text.RegularExpressions;
using ClawSharp.AgentHost.Contracts;
using ClawSharp.AgentHost.Ipc;
using ClawSharp.AgentHost.Projects;

namespace ClawSharp.AgentHost.Review;

public sealed class WorkspaceReviewService
{
    private static readonly Regex HunkHeaderPattern =
        new(@"^@@ -(?<oldStart>\d+)(,(?<oldCount>\d+))? \+(?<newStart>\d+)(,(?<newCount>\d+))? @@", RegexOptions.Compiled);

    private readonly RecentProjectStore _recentProjectStore;

    public WorkspaceReviewService(RecentProjectStore recentProjectStore)
    {
        _recentProjectStore = recentProjectStore;
    }

    public async Task<ListChangedFilesResponse> ListChangedFilesAsync(
        ListChangedFilesRequest request,
        CancellationToken cancellationToken = default)
    {
        var projectPath = await ResolveProjectPathAsync(request.ProjectId, cancellationToken);
        var statusResult = await RunGitAsync(projectPath, ["status", "--porcelain=v1", "--untracked-files=all"], cancellationToken);
        if (statusResult.ExitCode != 0)
        {
            throw new AgentHostException("review_failed", statusResult.Stderr);
        }

        var numStatByPath = await LoadNumStatByPathAsync(projectPath, cancellationToken);
        var files = statusResult.Stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => ParseChangedFile(projectPath, line, request.ThreadId, numStatByPath))
            .Where(static file => file is not null)
            .Select(static file => file!)
            .ToArray();

        return new ListChangedFilesResponse(request.ProjectId, request.ThreadId, files);
    }

    public async Task<GetDiffResponse> GetDiffAsync(
        GetDiffRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            throw new AgentHostException("invalid_request", "filePath is required.");
        }

        var projectPath = await ResolveProjectPathAsync(request.ProjectId, cancellationToken);
        var relativePath = request.FilePath.Replace('\\', '/');
        var diffText = await LoadDiffAsync(projectPath, relativePath, cancellationToken);
        var parsed = ParseUnifiedDiff(relativePath, diffText);
        return new GetDiffResponse(request.ProjectId, request.ThreadId, parsed);
    }

    private async Task<string> ResolveProjectPathAsync(string projectId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new AgentHostException("invalid_request", "projectId is required.");
        }

        var project = await _recentProjectStore.FindByIdAsync(projectId, cancellationToken);
        if (project is null)
        {
            throw new AgentHostException("project_not_found", $"Project '{projectId}' is not known to AgentHost yet.");
        }

        return project.Path;
    }

    private async Task<IReadOnlyDictionary<string, (int Additions, int Deletions)>> LoadNumStatByPathAsync(
        string projectPath,
        CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(projectPath, ["diff", "--numstat", "--find-renames", "HEAD"], cancellationToken);
        if (result.ExitCode != 0)
        {
            return new Dictionary<string, (int Additions, int Deletions)>(StringComparer.Ordinal);
        }

        var entries = new Dictionary<string, (int Additions, int Deletions)>(StringComparer.Ordinal);
        foreach (var line in result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length < 3)
            {
                continue;
            }

            var path = parts[2].Trim();
            entries[path] = (
                ParseNumStatValue(parts[0]),
                ParseNumStatValue(parts[1]));
        }

        return entries;
    }

    private async Task<string> LoadDiffAsync(string projectPath, string relativePath, CancellationToken cancellationToken)
    {
        var trackedDiff = await RunGitAsync(
            projectPath,
            ["diff", "--no-ext-diff", "--relative", "HEAD", "--", relativePath],
            cancellationToken);
        if (trackedDiff.ExitCode == 0 && !string.IsNullOrWhiteSpace(trackedDiff.Stdout))
        {
            return trackedDiff.Stdout;
        }

        var absolutePath = Path.Combine(projectPath, relativePath);
        if (!File.Exists(absolutePath))
        {
            return trackedDiff.Stdout;
        }

        var emptyFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(emptyFile, string.Empty, cancellationToken);
            var untrackedDiff = await RunGitAsync(
                projectPath,
                ["diff", "--no-index", "--no-ext-diff", "--", emptyFile, absolutePath],
                cancellationToken);
            return untrackedDiff.Stdout;
        }
        finally
        {
            try
            {
                File.Delete(emptyFile);
            }
            catch
            {
                // Best effort temp cleanup.
            }
        }
    }

    private static ChangedFileDto? ParseChangedFile(
        string projectPath,
        string line,
        string? threadId,
        IReadOnlyDictionary<string, (int Additions, int Deletions)> numStatByPath)
    {
        if (line.Length < 4)
        {
            return null;
        }

        var statusCode = line[..2].Trim();
        var path = line[3..].Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalizedPath = path.Replace('\\', '/');
        var status = statusCode switch
        {
            var code when code.Contains('A') || code == "??" => "A",
            var code when code.Contains('D') => "D",
            _ => "M"
        };
        var stats = numStatByPath.TryGetValue(normalizedPath, out var numStat)
            ? numStat
            : (status == "A" ? CountUntrackedFileLines(Path.Combine(projectPath, normalizedPath)) : (0, 0));

        return new ChangedFileDto(
            normalizedPath,
            status,
            stats.Item1,
            stats.Item2,
            threadId ?? string.Empty);
    }

    private static (int Additions, int Deletions) CountUntrackedFileLines(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return (0, 0);
            }

            var additions = File.ReadAllLines(path).Length;
            return (additions, 0);
        }
        catch
        {
            return (0, 0);
        }
    }

    private static int ParseNumStatValue(string raw)
    {
        return int.TryParse(raw, out var value) ? value : 0;
    }

    private static FileDiffDto ParseUnifiedDiff(string filePath, string diffText)
    {
        if (string.IsNullOrWhiteSpace(diffText))
        {
            return new FileDiffDto(filePath, []);
        }

        var hunks = new List<DiffHunkDto>();
        string? currentHeader = null;
        List<DiffLineDto>? currentLines = null;
        var oldLine = 0;
        var newLine = 0;

        foreach (var rawLine in diffText.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                if (currentHeader is not null && currentLines is not null)
                {
                    hunks.Add(new DiffHunkDto(currentHeader, currentLines.ToArray()));
                }

                currentHeader = line;
                currentLines = [];
                var match = HunkHeaderPattern.Match(line);
                oldLine = match.Success ? int.Parse(match.Groups["oldStart"].Value) : 0;
                newLine = match.Success ? int.Parse(match.Groups["newStart"].Value) : 0;
                continue;
            }

            if (currentLines is null ||
                line.StartsWith("diff --git", StringComparison.Ordinal) ||
                line.StartsWith("index ", StringComparison.Ordinal) ||
                line.StartsWith("--- ", StringComparison.Ordinal) ||
                line.StartsWith("+++ ", StringComparison.Ordinal) ||
                line.StartsWith("new file mode", StringComparison.Ordinal) ||
                line.StartsWith("deleted file mode", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal))
            {
                currentLines.Add(new DiffLineDto("add", line[1..], null, newLine++));
                continue;
            }

            if (line.StartsWith('-') && !line.StartsWith("---", StringComparison.Ordinal))
            {
                currentLines.Add(new DiffLineDto("del", line[1..], oldLine++, null));
                continue;
            }

            currentLines.Add(new DiffLineDto("context", line.StartsWith(' ') ? line[1..] : line, oldLine++, newLine++));
        }

        if (currentHeader is not null && currentLines is not null)
        {
            hunks.Add(new DiffHunkDto(currentHeader, currentLines.ToArray()));
        }

        return new FileDiffDto(filePath, hunks);
    }

    private static async Task<GitCommandResult> RunGitAsync(
        string projectPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = projectPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return new GitCommandResult(-1, string.Empty, "Failed to start git.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return new GitCommandResult(process.ExitCode, await stdoutTask, await stderrTask);
        }
        catch (Exception error)
        {
            return new GitCommandResult(-1, string.Empty, error.Message);
        }
    }

    private sealed record GitCommandResult(int ExitCode, string Stdout, string Stderr);
}
