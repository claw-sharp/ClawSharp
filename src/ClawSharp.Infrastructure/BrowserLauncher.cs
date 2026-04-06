// TS origin: ./utils/browser.ts
namespace ClawSharp.Infrastructure;

public static class BrowserLauncher
{
    public static async Task<bool> OpenPathAsync(
        string path,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<ProcessExecutionResult>>? executeAsync = null,
        CancellationToken cancellationToken = default)
    {
        executeAsync ??= static (fileName, args, token) => ProcessExecutionUtilities.ExecuteAsync(fileName, args, cancellationToken: token);

        try
        {
            var (command, arguments) = GetOpenPathCommand(path);
            var result = await executeAsync(command, arguments, cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> OpenBrowserAsync(
        string url,
        string? browserOverride = null,
        Func<string, IReadOnlyList<string>, CancellationToken, Task<ProcessExecutionResult>>? executeAsync = null,
        CancellationToken cancellationToken = default)
    {
        executeAsync ??= static (fileName, args, token) => ProcessExecutionUtilities.ExecuteAsync(fileName, args, cancellationToken: token);

        try
        {
            ValidateUrl(url);
            var (command, arguments) = GetOpenBrowserCommand(url, browserOverride ?? Environment.GetEnvironmentVariable("BROWSER"));
            var result = await executeAsync(command, arguments, cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public static void ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            throw new InvalidOperationException($"Invalid URL format: {url}");
        }

        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Invalid URL protocol: must use http:// or https://, got {parsed.Scheme}:");
        }
    }

    public static (string Command, IReadOnlyList<string> Arguments) GetOpenPathCommand(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return ("explorer", [path]);
        }

        return OperatingSystem.IsMacOS()
            ? ("open", [path])
            : ("xdg-open", [path]);
    }

    public static (string Command, IReadOnlyList<string> Arguments) GetOpenBrowserCommand(string url, string? browserOverride)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!string.IsNullOrWhiteSpace(browserOverride))
            {
                var parsed = ProcessExecutionUtilities.ParseCommandString(browserOverride);
                if (parsed.Count == 0)
                {
                    throw new InvalidOperationException("Invalid BROWSER command.");
                }

                return (parsed[0], parsed.Skip(1).Concat([url]).ToArray());
            }

            return ("rundll32", ["url,OpenURL", url]);
        }

        if (!string.IsNullOrWhiteSpace(browserOverride))
        {
            var parsed = ProcessExecutionUtilities.ParseCommandString(browserOverride);
            if (parsed.Count == 0)
            {
                throw new InvalidOperationException("Invalid BROWSER command.");
            }

            return (parsed[0], parsed.Skip(1).Concat([url]).ToArray());
        }

        return OperatingSystem.IsMacOS()
            ? ("open", [url])
            : ("xdg-open", [url]);
    }
}
