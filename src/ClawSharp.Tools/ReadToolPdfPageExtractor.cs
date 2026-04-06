using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using ClawSharp.Core;

namespace ClawSharp.Tools;

public static class ReadToolPdfPageExtractor
{
    private static Func<string, string[], int, CancellationToken, Task<ReadToolProcessResult>>? _processRunnerOverride;
    private static bool? _pdftoppmAvailable;

    public static void ResetForTesting()
    {
        _processRunnerOverride = null;
        _pdftoppmAvailable = null;
    }

    public static void SetProcessRunnerForTesting(
        Func<string, string[], int, CancellationToken, Task<ReadToolProcessResult>>? processRunner)
    {
        _processRunnerOverride = processRunner;
        _pdftoppmAvailable = null;
    }

    public static async Task<int?> GetPdfPageCountAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var result = await RunProcessAsync("pdfinfo", [filePath], 10_000, cancellationToken);
        if (result.ExitCode != 0)
        {
            return null;
        }

        var match = Regex.Match(result.StandardOutput, @"^Pages:\s+(\d+)", RegexOptions.Multiline);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var count))
        {
            return null;
        }

        return count;
    }

    public static async Task<PdfExtractPagesResult> ExtractPagesAsync(
        string filePath,
        ConversationSession session,
        PdfPageRange? range = null,
        CancellationToken cancellationToken = default)
    {
        var originalSize = new FileInfo(filePath).Length;
        if (originalSize == 0)
        {
            throw new InvalidOperationException($"PDF file is empty: {filePath}");
        }

        if (originalSize > ReadToolPolicies.PdfMaxExtractSizeBytes)
        {
            throw new InvalidOperationException(
                $"PDF file exceeds maximum allowed size for text extraction ({FormatFileSize(ReadToolPolicies.PdfMaxExtractSizeBytes)}).");
        }

        if (!await IsPdftoppmAvailableAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "pdftoppm is not installed. Install poppler-utils (e.g. `brew install poppler` or `apt-get install poppler-utils`) to enable PDF page rendering.");
        }

        var outputDir = Path.Combine(GetToolResultsDir(session), $"pdf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);

        var prefix = Path.Combine(outputDir, "page");
        var arguments = new List<string> { "-jpeg", "-r", "100" };
        if (range?.FirstPage is int firstPage)
        {
            arguments.Add("-f");
            arguments.Add(firstPage.ToString());
        }

        if (range is not null && range.LastPage != int.MaxValue)
        {
            arguments.Add("-l");
            arguments.Add(range.LastPage.ToString());
        }

        arguments.Add(filePath);
        arguments.Add(prefix);

        var result = await RunProcessAsync("pdftoppm", [.. arguments], 120_000, cancellationToken);
        if (result.ExitCode != 0)
        {
            if (result.StandardError.Contains("password", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "PDF is password-protected. Please provide an unprotected version.");
            }

            if (Regex.IsMatch(result.StandardError, "damaged|corrupt|invalid", RegexOptions.IgnoreCase))
            {
                throw new InvalidOperationException("PDF file is corrupted or invalid.");
            }

            throw new InvalidOperationException($"pdftoppm failed: {result.StandardError}");
        }

        var imageFiles = Directory
            .EnumerateFiles(outputDir, "*.jpg", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (imageFiles.Length == 0)
        {
            throw new InvalidOperationException(
                "pdftoppm produced no output pages. The PDF may be invalid.");
        }

        return new PdfExtractPagesResult(outputDir, imageFiles.Length, originalSize);
    }

    private static async Task<bool> IsPdftoppmAvailableAsync(CancellationToken cancellationToken)
    {
        if (_pdftoppmAvailable is not null)
        {
            return _pdftoppmAvailable.Value;
        }

        var result = await RunProcessAsync("pdftoppm", ["-v"], 5_000, cancellationToken);
        _pdftoppmAvailable = result.ExitCode == 0 || result.StandardError.Length > 0;
        return _pdftoppmAvailable.Value;
    }

    private static async Task<ReadToolProcessResult> RunProcessAsync(
        string fileName,
        string[] arguments,
        int timeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        if (_processRunnerOverride is not null)
        {
            return await _processRunnerOverride(fileName, arguments, timeoutMilliseconds, cancellationToken);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return new ReadToolProcessResult(-1, string.Empty, string.Empty);
            }
        }
        catch
        {
            return new ReadToolProcessResult(-1, string.Empty, string.Empty);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMilliseconds);
        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            return new ReadToolProcessResult(
                process.ExitCode,
                await stdoutTask,
                await stderrTask);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            return new ReadToolProcessResult(-1, string.Empty, "Process timed out.");
        }
    }

    private static string GetToolResultsDir(ConversationSession session)
    {
        return Path.Combine(
            SessionStoragePaths.GetProjectDir(session.ProjectDirectory),
            session.Id,
            "tool-results");
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
}

public sealed record ReadToolProcessResult(int ExitCode, string StandardOutput, string StandardError);
public sealed record PdfExtractPagesResult(string OutputDir, int Count, long OriginalSize);
