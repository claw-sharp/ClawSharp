using System.Text;

namespace ClawSharp.Infrastructure;

public sealed record DeepLinkAction(string? Query = null, string? Cwd = null, string? Repo = null);

public sealed class DeepLinkProtocolService
{
    public const string Protocol = "claude-cli";
    public const string WindowsRegistryKey = @"HKEY_CURRENT_USER\Software\Classes\claude-cli";

    private const int MaxQueryLength = 5000;
    private const int MaxCwdLength = 4096;

    private readonly Func<string, IReadOnlyList<string>, CancellationToken, Task<ProcessExecutionResult>> _executeAsync;

    public DeepLinkProtocolService(
        Func<string, IReadOnlyList<string>, CancellationToken, Task<ProcessExecutionResult>>? executeAsync = null)
    {
        _executeAsync = executeAsync ?? ((fileName, args, token) =>
            ProcessExecutionUtilities.ExecuteAsync(fileName, args, cancellationToken: token));
    }

    public DeepLinkAction Parse(string uri)
    {
        var normalized = uri.StartsWith($"{Protocol}://", StringComparison.Ordinal)
            ? uri
            : uri.StartsWith($"{Protocol}:", StringComparison.Ordinal)
                ? uri.Replace($"{Protocol}:", $"{Protocol}://", StringComparison.Ordinal)
                : null;

        if (normalized is null)
        {
            throw new InvalidOperationException(
                $"Invalid deep link: expected {Protocol}:// scheme, got \"{uri}\"");
        }

        if (!Uri.TryCreate(normalized, UriKind.Absolute, out var parsed))
        {
            throw new InvalidOperationException($"Invalid deep link URL: \"{uri}\"");
        }

        if (!string.Equals(parsed.Host, "open", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unknown deep link action: \"{parsed.Host}\"");
        }

        var queryParameters = System.Web.HttpUtility.ParseQueryString(parsed.Query);
        var cwd = queryParameters["cwd"];
        var repo = queryParameters["repo"];
        var rawQuery = queryParameters["q"];

        if (!string.IsNullOrWhiteSpace(cwd))
        {
            if (!Path.IsPathRooted(cwd))
            {
                throw new InvalidOperationException(
                    $"Invalid cwd in deep link: must be an absolute path, got \"{cwd}\"");
            }

            if (ContainsControlCharacters(cwd))
            {
                throw new InvalidOperationException("Deep link cwd contains disallowed control characters");
            }

            if (cwd.Length > MaxCwdLength)
            {
                throw new InvalidOperationException(
                    $"Deep link cwd exceeds {MaxCwdLength} characters (got {cwd.Length})");
            }
        }

        if (!string.IsNullOrWhiteSpace(repo) && !IsValidRepoSlug(repo))
        {
            throw new InvalidOperationException(
                $"Invalid repo in deep link: expected \"owner/repo\", got \"{repo}\"");
        }

        string? query = null;
        if (!string.IsNullOrWhiteSpace(rawQuery))
        {
            query = SanitizeUnicode(rawQuery.Trim());
            if (ContainsControlCharacters(query))
            {
                throw new InvalidOperationException("Deep link query contains disallowed control characters");
            }

            if (query.Length > MaxQueryLength)
            {
                throw new InvalidOperationException(
                    $"Deep link query exceeds {MaxQueryLength} characters (got {query.Length})");
            }
        }

        return new DeepLinkAction(query, cwd, repo);
    }

    public string Build(DeepLinkAction action)
    {
        var builder = new UriBuilder($"{Protocol}://open");
        var query = System.Web.HttpUtility.ParseQueryString(string.Empty);
        if (!string.IsNullOrWhiteSpace(action.Query))
        {
            query["q"] = action.Query;
        }

        if (!string.IsNullOrWhiteSpace(action.Cwd))
        {
            query["cwd"] = action.Cwd;
        }

        if (!string.IsNullOrWhiteSpace(action.Repo))
        {
            query["repo"] = action.Repo;
        }

        builder.Query = query.ToString() ?? string.Empty;
        return builder.Uri.ToString();
    }

    public async Task<bool> RegisterProtocolHandlerAsync(
        string executablePath,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var commandValue = BuildWindowsCommandValue(executablePath);
        foreach (var args in new[]
                 {
                     new[] { "add", WindowsRegistryKey, "/ve", "/d", "URL:Claude Code URL Handler", "/f" },
                     new[] { "add", WindowsRegistryKey, "/v", "URL Protocol", "/d", string.Empty, "/f" },
                     new[] { "add", $@"{WindowsRegistryKey}\shell\open\command", "/ve", "/d", commandValue, "/f" }
                 })
        {
            var result = await _executeAsync("reg", args, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                return false;
            }
        }

        return true;
    }

    public async Task<bool> IsProtocolHandlerCurrentAsync(
        string executablePath,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var result = await _executeAsync(
            "reg",
            ["query", $@"{WindowsRegistryKey}\shell\open\command", "/ve"],
            cancellationToken).ConfigureAwait(false);

        return result.ExitCode == 0 &&
               result.Stdout.Contains(BuildWindowsCommandValue(executablePath), StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildWindowsCommandValue(string executablePath)
    {
        return $"\"{executablePath}\" --handle-uri \"%1\"";
    }

    private static bool ContainsControlCharacters(string value)
    {
        return value.Any(static character => char.IsControl(character));
    }

    private static bool IsValidRepoSlug(string value)
    {
        var slashIndex = value.IndexOf('/');
        return slashIndex > 0 &&
               slashIndex == value.LastIndexOf('/') &&
               value.All(static character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_' or '/');
    }

    private static string SanitizeUnicode(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormKC))
        {
            if (character == '\u200B' ||
                character == '\u200C' ||
                character == '\u200D' ||
                character == '\uFEFF')
            {
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
