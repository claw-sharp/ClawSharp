using System.Text.RegularExpressions;

namespace ClawSharp.Infrastructure;

public static partial class WindowsPathConversion
{
    [GeneratedRegex(@"^([A-Za-z]):[/\\]")]
    private static partial Regex WindowsDrivePathRegex();

    [GeneratedRegex(@"^/cygdrive/([A-Za-z])(/|$)")]
    private static partial Regex CygdrivePathRegex();

    [GeneratedRegex(@"^/([A-Za-z])(/|$)")]
    private static partial Regex PosixDrivePathRegex();

    public static string WindowsPathToPosixPath(string windowsPath)
    {
        if (windowsPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return windowsPath.Replace('\\', '/');
        }

        var driveMatch = WindowsDrivePathRegex().Match(windowsPath);
        if (driveMatch.Success)
        {
            var driveLetter = char.ToLowerInvariant(driveMatch.Groups[1].Value[0]);
            return "/" + driveLetter + windowsPath[2..].Replace('\\', '/');
        }

        return windowsPath.Replace('\\', '/');
    }

    public static string PosixPathToWindowsPath(string posixPath)
    {
        if (posixPath.StartsWith("//", StringComparison.Ordinal))
        {
            return posixPath.Replace('/', '\\');
        }

        var cygdriveMatch = CygdrivePathRegex().Match(posixPath);
        if (cygdriveMatch.Success)
        {
            var driveLetter = char.ToUpperInvariant(cygdriveMatch.Groups[1].Value[0]);
            var rest = posixPath[("/cygdrive/" + cygdriveMatch.Groups[1].Value).Length..];
            return driveLetter + ":" + (string.IsNullOrEmpty(rest) ? @"\" : rest.Replace('/', '\\'));
        }

        var driveMatch = PosixDrivePathRegex().Match(posixPath);
        if (driveMatch.Success)
        {
            var driveLetter = char.ToUpperInvariant(driveMatch.Groups[1].Value[0]);
            var rest = posixPath[2..];
            return driveLetter + ":" + (string.IsNullOrEmpty(rest) ? @"\" : rest.Replace('/', '\\'));
        }

        return posixPath.Replace('/', '\\');
    }
}
