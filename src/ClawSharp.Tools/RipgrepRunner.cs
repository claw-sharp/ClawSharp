// TS origin: ./utils/ripgrep.ts, ./utils/glob.ts
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ClawSharp.Tools;

internal static class RipgrepRunner
{
    public static async Task<IReadOnlyList<string>> RunLinesAsync(
        IReadOnlyList<string> arguments,
        string target,
        CancellationToken cancellationToken = default)
    {
        var fullTarget = Path.GetFullPath(target);
        var workingDirectory = Directory.Exists(fullTarget)
            ? fullTarget
            : Path.GetDirectoryName(fullTarget) ?? Environment.CurrentDirectory;

        try
        {
            return await RunProcessAsync(arguments, fullTarget, workingDirectory, cancellationToken);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return await RunManagedFallbackAsync(arguments, fullTarget, cancellationToken);
        }
    }

    private static async Task<IReadOnlyList<string>> RunProcessAsync(
        IReadOnlyList<string> arguments,
        string fullTarget,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "rg",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.ArgumentList.Add(fullTarget);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException("ripgrep unavailable", exception);
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode is not 0 and not 1)
        {
            var message = string.IsNullOrWhiteSpace(stderr)
                ? $"ripgrep exited with code {process.ExitCode}."
                : stderr.Trim();
            throw new InvalidOperationException(message);
        }

        return SplitLines(stdout);
    }

    private static Task<IReadOnlyList<string>> RunManagedFallbackAsync(
        IReadOnlyList<string> arguments,
        string fullTarget,
        CancellationToken cancellationToken)
    {
        if (arguments.Contains("--files", StringComparer.Ordinal))
        {
            return Task.FromResult<IReadOnlyList<string>>(RunManagedGlob(arguments, fullTarget, cancellationToken));
        }

        return Task.FromResult<IReadOnlyList<string>>(RunManagedGrep(arguments, fullTarget, cancellationToken));
    }

    private static IReadOnlyList<string> RunManagedGlob(
        IReadOnlyList<string> arguments,
        string fullTarget,
        CancellationToken cancellationToken)
    {
        var globIndex = IndexOf(arguments, "--glob");
        if (globIndex < 0 || globIndex + 1 >= arguments.Count)
        {
            throw new InvalidOperationException("Glob requires a --glob pattern.");
        }

        var pattern = arguments[globIndex + 1];
        var root = Directory.Exists(fullTarget)
            ? fullTarget
            : Path.GetDirectoryName(fullTarget) ?? Environment.CurrentDirectory;
        var matcher = BuildGlobRegex(pattern);

        return Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => matcher.IsMatch(Path.GetRelativePath(root, path).Replace('\\', '/')))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .ToArray();
    }

    private static IReadOnlyList<string> RunManagedGrep(
        IReadOnlyList<string> arguments,
        string fullTarget,
        CancellationToken cancellationToken)
    {
        var options = ParseGrepArguments(arguments);
        var files = EnumerateTargetFiles(fullTarget, options.Type, options.Globs).ToArray();
        var regexOptions = RegexOptions.CultureInvariant;
        if (options.CaseInsensitive)
        {
            regexOptions |= RegexOptions.IgnoreCase;
        }

        if (options.Multiline)
        {
            regexOptions |= RegexOptions.Singleline | RegexOptions.Multiline;
        }

        var regex = new Regex(options.Pattern, regexOptions);
        var results = new List<string>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = File.ReadAllText(file);
            var displayPath = file.Replace('\\', '/');

            switch (options.Mode)
            {
                case GrepOutputMode.FilesWithMatches:
                    if (regex.IsMatch(content))
                    {
                        results.Add(displayPath);
                    }

                    break;

                case GrepOutputMode.Count:
                {
                    var count = SplitContentLines(content).Count(line => regex.IsMatch(line));
                    if (count > 0)
                    {
                        results.Add($"{displayPath}:{count}");
                    }

                    break;
                }

                default:
                {
                    var lineNumber = 0;
                    foreach (var line in SplitContentLines(content))
                    {
                        lineNumber++;
                        if (!regex.IsMatch(line))
                        {
                            continue;
                        }

                        results.Add(options.ShowLineNumbers
                            ? $"{displayPath}:{lineNumber}:{line}"
                            : $"{displayPath}:{line}");
                    }

                    break;
                }
            }
        }

        return results;
    }

    private static IEnumerable<string> EnumerateTargetFiles(
        string fullTarget,
        string? type,
        IReadOnlyList<string> globs)
    {
        if (File.Exists(fullTarget))
        {
            yield return fullTarget;
            yield break;
        }

        var root = Directory.Exists(fullTarget)
            ? fullTarget
            : Path.GetDirectoryName(fullTarget) ?? Environment.CurrentDirectory;
        var globMatchers = globs.Select(BuildGlobRegex).ToArray();
        var extensionFilter = GetTypeExtension(type);

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (extensionFilter is not null &&
                !file.EndsWith(extensionFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (globMatchers.Length > 0 && !globMatchers.Any(matcher => matcher.IsMatch(relativePath)))
            {
                continue;
            }

            yield return file;
        }
    }

    private static string? GetTypeExtension(string? type)
    {
        return type?.ToLowerInvariant() switch
        {
            "cs" => ".cs",
            "ts" => ".ts",
            "js" => ".js",
            "json" => ".json",
            "md" => ".md",
            "txt" => ".txt",
            _ => null
        };
    }

    private static Regex BuildGlobRegex(string pattern)
    {
        var normalized = pattern.Replace('\\', '/');
        var builder = new System.Text.StringBuilder("^");

        for (var index = 0; index < normalized.Length; index++)
        {
            var current = normalized[index];
            if (current == '*')
            {
                var nextIsStar = index + 1 < normalized.Length && normalized[index + 1] == '*';
                if (nextIsStar)
                {
                    var followedBySlash = index + 2 < normalized.Length && normalized[index + 2] == '/';
                    builder.Append(followedBySlash ? "(?:.*/)?" : ".*");
                    index += followedBySlash ? 2 : 1;
                    continue;
                }

                builder.Append("[^/]*");
                continue;
            }

            builder.Append(current switch
            {
                '?' => "[^/]",
                '.' => "\\.",
                '/' => "/",
                _ => Regex.Escape(current.ToString())
            });
        }

        builder.Append('$');
        return new Regex(builder.ToString(), RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    private static ManagedGrepArguments ParseGrepArguments(IReadOnlyList<string> arguments)
    {
        string? pattern = null;
        string? type = null;
        var globs = new List<string>();
        var showLineNumbers = false;
        var caseInsensitive = false;
        var multiline = false;
        var mode = GrepOutputMode.Content;

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            switch (argument)
            {
                case "-i":
                    caseInsensitive = true;
                    break;
                case "-U":
                case "--multiline-dotall":
                    multiline = true;
                    break;
                case "-l":
                    mode = GrepOutputMode.FilesWithMatches;
                    break;
                case "-c":
                    mode = GrepOutputMode.Count;
                    break;
                case "-n":
                    showLineNumbers = true;
                    break;
                case "--type":
                    if (index + 1 < arguments.Count)
                    {
                        type = arguments[++index];
                    }

                    break;
                case "--glob":
                    if (index + 1 < arguments.Count)
                    {
                        globs.Add(arguments[++index]);
                    }

                    break;
                case "-e":
                    if (index + 1 < arguments.Count)
                    {
                        pattern = arguments[++index];
                    }

                    break;
                case "--hidden":
                case "-H":
                case "--max-columns":
                case "-C":
                case "-A":
                case "-B":
                    if (argument is "--max-columns" or "-C" or "-A" or "-B")
                    {
                        index++;
                    }

                    break;
                default:
                    if (!argument.StartsWith("-", StringComparison.Ordinal) && pattern is null)
                    {
                        pattern = argument;
                    }

                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(pattern))
        {
            throw new InvalidOperationException("Grep requires a pattern.");
        }

        return new ManagedGrepArguments(pattern, type, globs, showLineNumbers, caseInsensitive, multiline, mode);
    }

    private static int IndexOf(IReadOnlyList<string> values, string candidate)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (string.Equals(values[index], candidate, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static IReadOnlyList<string> SplitLines(string content)
    {
        return content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private static IReadOnlyList<string> SplitContentLines(string content)
    {
        return content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
    }

    private sealed record ManagedGrepArguments(
        string Pattern,
        string? Type,
        IReadOnlyList<string> Globs,
        bool ShowLineNumbers,
        bool CaseInsensitive,
        bool Multiline,
        GrepOutputMode Mode);

    private enum GrepOutputMode
    {
        Content,
        FilesWithMatches,
        Count
    }
}
