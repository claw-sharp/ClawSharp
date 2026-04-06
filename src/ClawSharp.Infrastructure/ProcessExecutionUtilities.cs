using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace ClawSharp.Infrastructure;

public sealed record ProcessExecutionResult(
    int ExitCode,
    string Stdout,
    string Stderr);

internal static class ProcessExecutionUtilities
{
    public static async Task<ProcessExecutionResult> ExecuteAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                if (pair.Value is null)
                {
                    startInfo.Environment.Remove(pair.Key);
                }
                else
                {
                    startInfo.Environment[pair.Key] = pair.Value;
                }
            }
        }

        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return new ProcessExecutionResult(-1, string.Empty, $"Failed to start {fileName}.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            return new ProcessExecutionResult(process.ExitCode, stdout, stderr);
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or IOException)
        {
            return new ProcessExecutionResult(-1, string.Empty, exception.Message);
        }
    }

    public static IReadOnlyList<string> ParseCommandString(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return [];
        }

        var segments = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;

        foreach (var character in command)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (builder.Length > 0)
                {
                    segments.Add(builder.ToString());
                    builder.Clear();
                }

                continue;
            }

            builder.Append(character);
        }

        if (builder.Length > 0)
        {
            segments.Add(builder.ToString());
        }

        return segments;
    }
}
