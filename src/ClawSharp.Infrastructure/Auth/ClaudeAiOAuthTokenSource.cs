// TS parity status: ports the Claude AI OAuth token file-descriptor and CCR well-known-file fallback path used by the TypeScript auth and retry flows; logging and shared process-global bootstrap caches remain intentionally unported.
namespace ClawSharp.Infrastructure;

public sealed class ClaudeAiOAuthTokenSource
{
    public const string CcrOAuthTokenPath = "/home/claude/.clawsharp/remote/.oauth_token";

    private readonly string _wellKnownTokenPath;
    private readonly Func<string, string?> _getEnvironmentVariable;

    private bool _cached;
    private string? _cachedToken;

    public ClaudeAiOAuthTokenSource(
        string? wellKnownTokenPath = null,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        _wellKnownTokenPath = wellKnownTokenPath ?? CcrOAuthTokenPath;
        _getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    }

    public string? ReadToken()
    {
        if (_cached)
        {
            return _cachedToken;
        }

        var descriptorValue = _getEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR");
        if (!string.IsNullOrWhiteSpace(descriptorValue))
        {
            var tokenFromDescriptor = TryReadTokenFromDescriptor(descriptorValue);
            if (!string.IsNullOrWhiteSpace(tokenFromDescriptor))
            {
                Cache(tokenFromDescriptor);
                MaybePersistForSubprocesses(tokenFromDescriptor);
                return tokenFromDescriptor;
            }
        }

        var tokenFromWellKnownFile = TryReadTokenFromWellKnownFile();
        Cache(tokenFromWellKnownFile);
        return tokenFromWellKnownFile;
    }

    private string? TryReadTokenFromDescriptor(string descriptorValue)
    {
        if (!int.TryParse(descriptorValue, out var descriptor))
        {
            return null;
        }

        var descriptorPath = GetDescriptorPath(descriptor);
        if (descriptorPath is null || !File.Exists(descriptorPath))
        {
            return null;
        }

        try
        {
            var token = File.ReadAllText(descriptorPath).Trim();
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }
        catch
        {
            return null;
        }
    }

    private string? TryReadTokenFromWellKnownFile()
    {
        if (!File.Exists(_wellKnownTokenPath))
        {
            return null;
        }

        try
        {
            var token = File.ReadAllText(_wellKnownTokenPath).Trim();
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }
        catch
        {
            return null;
        }
    }

    private void MaybePersistForSubprocesses(string token)
    {
        if (!IsTruthy(_getEnvironmentVariable("CLAUDE_CODE_REMOTE")))
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(_wellKnownTokenPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_wellKnownTokenPath, token);
        }
        catch
        {
            // TS path treats subprocess persistence as best effort.
        }
    }

    private static string? GetDescriptorPath(int descriptor)
    {
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
        {
            return $"/dev/fd/{descriptor}";
        }

        if (OperatingSystem.IsLinux())
        {
            return $"/proc/self/fd/{descriptor}";
        }

        return null;
    }

    private static bool IsTruthy(string? value)
    {
        return string.Equals(value, "1", StringComparison.Ordinal) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }

    private void Cache(string? token)
    {
        _cached = true;
        _cachedToken = token;
    }
}
