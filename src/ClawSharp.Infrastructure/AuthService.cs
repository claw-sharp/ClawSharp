// TS origin: utils/auth.ts, utils/model/providers.ts
using System.Collections.Concurrent;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

/// <summary>
/// Implementation of authentication and provider checks.
/// TS origin: utils/auth.ts, utils/model/providers.ts
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly ClaudeAiOAuthTokenSource _tokenSource;
    private readonly ClawSharpSettings _settings;
    private readonly ConcurrentDictionary<string, OAuthTokens?> _tokenCache = new();

    public AuthService(ClaudeAiOAuthTokenSource tokenSource, ClawSharpSettings settings)
    {
        _tokenSource = tokenSource;
        _settings = settings;
    }

    public bool IsAnthropicAuthEnabled()
    {
        // TS origin: isAnthropicAuthEnabled in utils/auth.ts
        
        // Equivalent to isBareMode() check often used in TS
        if (Environment.GetCommandLineArgs().Contains("--bare")) 
        {
            return false;
        }

        if (IsUsing3PServices())
        {
            return false;
        }

        if (!IsFirstPartyAnthropicBaseUrl())
        {
            return false;
        }

        // Check for external API key or token that would disable managed OAuth
        // (Simplified for now, matching the most common paths)
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN")) ||
            !string.IsNullOrEmpty(_settings.ClaudeApiKey) ||
            !string.IsNullOrEmpty(_settings.ApiKeyHelper))
        {
            // Unless it's a managed context where OAuth is forced
            if (!IsManagedOAuthContext())
            {
                return false;
            }
        }

        return true;
    }

    public bool IsClaudeAISubscriber()
    {
        if (!IsAnthropicAuthEnabled())
        {
            return false;
        }

        var tokens = GetClaudeAIOAuthTokens();
        if (tokens == null)
        {
            return false;
        }

        // TS: Boolean(scopes?.includes('user:inference'))
        // Note: 'user:inference' is the CLAUDE_AI_INFERENCE_SCOPE constant in TS
        return tokens.Scopes.Contains("user:inference", StringComparer.Ordinal);
    }

    public bool IsUsing3PServices()
    {
        // TS origin: isUsing3PServices in utils/auth.ts
        return IsEnvTruthy(Environment.GetEnvironmentVariable("CLAUDE_CODE_USE_BEDROCK")) ||
               IsEnvTruthy(Environment.GetEnvironmentVariable("CLAUDE_CODE_USE_VERTEX")) ||
               IsEnvTruthy(Environment.GetEnvironmentVariable("CLAUDE_CODE_USE_FOUNDRY"));
    }

    public bool IsFirstPartyAnthropicBaseUrl()
    {
        // TS origin: isFirstPartyAnthropicBaseUrl in utils/model/providers.ts
        var baseUrl = Environment.GetEnvironmentVariable("ANTHROPIC_BASE_URL");
        if (string.IsNullOrEmpty(baseUrl))
        {
            return true;
        }

        try
        {
            var uri = new Uri(baseUrl);
            var host = uri.Host;
            var allowedHosts = new List<string> { "api.anthropic.com" };
            if (string.Equals(Environment.GetEnvironmentVariable("USER_TYPE"), "ant", StringComparison.Ordinal))
            {
                allowedHosts.Add("api-staging.anthropic.com");
            }
            return allowedHosts.Contains(host, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public OAuthTokens? GetClaudeAIOAuthTokens()
    {
        // TS origin: getClaudeAIOAuthTokens in utils/auth.ts
        
        // For now, we only support tokens from environment and file descriptors 
        // provided by ClaudeAiOAuthTokenSource. This covers CCR and remote sessions.
        // Full keychain storage support will be added in Phase 15.
        
        return _tokenCache.GetOrAdd("current", _ =>
        {
            var token = _tokenSource.ReadToken();
            if (string.IsNullOrEmpty(token))
            {
                return null;
            }

            // Defaults to inference scope for environment/descriptor tokens as per TS
            return new OAuthTokens(
                token!,
                null,
                null,
                new[] { "user:inference" },
                null,
                null);
        });
    }

    private static bool IsManagedOAuthContext()
    {
        // TS origin: isManagedOAuthContext in utils/auth.ts
        return IsEnvTruthy(Environment.GetEnvironmentVariable("CLAUDE_CODE_REMOTE")) ||
               string.Equals(Environment.GetEnvironmentVariable("CLAUDE_CODE_ENTRYPOINT"), "claude-desktop", StringComparison.Ordinal);
    }

    private static bool IsEnvTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim().ToLowerInvariant();
        return trimmed is "1" or "true" or "yes" or "on";
    }
}
