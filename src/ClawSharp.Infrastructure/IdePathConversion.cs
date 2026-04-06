// TS origin: ./utils/idePathConversion.ts
// TS parity status: ports the Windows<->WSL IDE path conversion and WSL UNC distro-match checks used by the current IDE integration flow.
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace ClawSharp.Infrastructure;

public interface IWindowsToWslPathConverter
{
    string ToLocalPath(string idePath);

    string ToIdePath(string localPath);
}

public sealed class WindowsToWslPathConverter : IWindowsToWslPathConverter
{
    private static readonly Regex WslUncPattern = new(
        @"^\\\\wsl(?:\.localhost|\$)\\([^\\]+)(.*)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DriveLetterPattern = new(
        @"^([A-Z]):",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly string? _wslDistroName;

    public WindowsToWslPathConverter(string? wslDistroName)
    {
        _wslDistroName = wslDistroName;
    }

    public string ToLocalPath(string idePath)
    {
        if (string.IsNullOrWhiteSpace(idePath))
        {
            return idePath;
        }

        if (!string.IsNullOrWhiteSpace(_wslDistroName))
        {
            var uncMatch = WslUncPattern.Match(idePath);
            if (uncMatch.Success &&
                !string.Equals(uncMatch.Groups[1].Value, _wslDistroName, StringComparison.Ordinal))
            {
                return idePath;
            }
        }

        var converted = TryRunWslPath("-u", idePath);
        if (!string.IsNullOrWhiteSpace(converted))
        {
            return converted;
        }

        var normalized = idePath.Replace('\\', '/');
        return DriveLetterPattern.Replace(
            normalized,
            static match => $"/mnt/{match.Groups[1].Value.ToLowerInvariant()}");
    }

    public string ToIdePath(string localPath)
    {
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return localPath;
        }

        var converted = TryRunWslPath("-w", localPath);
        return string.IsNullOrWhiteSpace(converted)
            ? localPath
            : converted;
    }

    public static bool CheckWslDistroMatch(string windowsPath, string wslDistroName)
    {
        var uncMatch = WslUncPattern.Match(windowsPath);
        return !uncMatch.Success || string.Equals(uncMatch.Groups[1].Value, wslDistroName, StringComparison.Ordinal);
    }

    private static string? TryRunWslPath(string mode, string path)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "wslpath",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(mode);
            startInfo.ArgumentList.Add(path);

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}
