// TS origin: ./services/mcp/auth.ts
using System.Text;

namespace ClawSharp.Infrastructure;

public static class McpAuthLoggingRedaction
{
    private static readonly HashSet<string> SensitiveOAuthParams = new(StringComparer.Ordinal)
    {
        "state",
        "nonce",
        "code_challenge",
        "code_verifier",
        "code"
    };

    public static string RedactSensitiveUrlParams(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsedUrl))
        {
            return url;
        }

        var builder = new UriBuilder(parsedUrl);
        var query = builder.Query;
        if (string.IsNullOrEmpty(query))
        {
            return builder.Uri.ToString();
        }

        var trimmedQuery = query[0] == '?' ? query[1..] : query;
        var parts = trimmedQuery.Split('&', StringSplitOptions.None);
        var redacted = new StringBuilder(trimmedQuery.Length);

        for (var index = 0; index < parts.Length; index++)
        {
            if (index > 0)
            {
                redacted.Append('&');
            }

            var part = parts[index];
            var separatorIndex = part.IndexOf('=');
            var rawKey = separatorIndex >= 0 ? part[..separatorIndex] : part;
            var decodedKey = Uri.UnescapeDataString(rawKey.Replace('+', ' '));

            if (!SensitiveOAuthParams.Contains(decodedKey))
            {
                redacted.Append(part);
                continue;
            }

            redacted.Append(rawKey);
            if (separatorIndex >= 0)
            {
                redacted.Append('=');
                redacted.Append(Uri.EscapeDataString("[REDACTED]"));
            }
        }

        builder.Query = redacted.ToString();
        return builder.Uri.ToString();
    }
}
