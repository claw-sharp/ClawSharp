// TS origin: ./services/mcp/auth.ts
using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class McpOAuthClientProvider
{
    private readonly string _serverName;
    private readonly McpServerConfig _serverConfig;
    private readonly IMcpSecureStorage _secureStorage;
    private readonly McpAuthStateService _authStateService;
    private string? _codeVerifier;
    private string? _authorizationUrl;
    private string? _pendingStepUpScope;

    public McpOAuthClientProvider(
        string serverName,
        McpServerConfig serverConfig,
        IMcpSecureStorage secureStorage,
        McpAuthStateService authStateService)
    {
        _serverName = serverName;
        _serverConfig = serverConfig;
        _secureStorage = secureStorage;
        _authStateService = authStateService;
    }

    public string? AuthorizationUrl => _authorizationUrl;

    public void MarkStepUpPending(string scope)
    {
        _pendingStepUpScope = string.IsNullOrWhiteSpace(scope) ? null : scope;
    }

    public McpOAuthClientInformation? ClientInformation()
    {
        var entry = _authStateService.GetOAuthEntry(_serverName, _serverConfig);
        var clientSecret =
            entry?.ClientSecret ??
            _authStateService.GetMcpClientConfig(_serverName, _serverConfig)?.ClientSecret;

        if (string.IsNullOrWhiteSpace(entry?.ClientId))
        {
            return null;
        }

        return new McpOAuthClientInformation(entry.ClientId!, clientSecret);
    }

    public void SaveClientInformation(McpOAuthClientInformation clientInformation)
    {
        ArgumentNullException.ThrowIfNull(clientInformation);

        var existing = _authStateService.GetOAuthEntry(_serverName, _serverConfig);
        var remoteUrl = GetRemoteUrl(_serverConfig);
        _authStateService.SaveOAuthEntry(
            _serverName,
            _serverConfig,
            new McpOAuthEntry(
                _serverName,
                remoteUrl,
                existing?.AccessToken ?? string.Empty,
                existing?.ExpiresAt ?? 0,
                existing?.RefreshToken,
                existing?.Scope,
                clientInformation.ClientId,
                clientInformation.ClientSecret,
                existing?.StepUpScope,
                existing?.DiscoveryState));
    }

    public async Task<McpOAuthTokens?> TokensAsync(CancellationToken cancellationToken = default)
    {
        var data = await _secureStorage.ReadAsync(cancellationToken).ConfigureAwait(false);
        var serverKey = McpAuthStateService.GetServerKey(_serverName, _serverConfig);
        var tokenData = data?.McpOAuth?.GetValueOrDefault(serverKey);
        if (tokenData is null || string.IsNullOrEmpty(tokenData.AccessToken))
        {
            return null;
        }

        var refreshToken = _pendingStepUpScope is null ? tokenData.RefreshToken : null;
        double? expiresIn = tokenData.ExpiresAt > 0
            ? (tokenData.ExpiresAt - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 1000d
            : null;

        return new McpOAuthTokens(
            tokenData.AccessToken!,
            refreshToken,
            expiresIn,
            tokenData.Scope);
    }

    public void SaveTokens(McpOAuthTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        var existing = _authStateService.GetOAuthEntry(_serverName, _serverConfig);
        var remoteUrl = GetRemoteUrl(_serverConfig);
        var expiresAt = tokens.ExpiresIn.HasValue
            ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)Math.Round(tokens.ExpiresIn.Value * 1000)
            : existing?.ExpiresAt ?? 0;

        _authStateService.SaveOAuthEntry(
            _serverName,
            _serverConfig,
            new McpOAuthEntry(
                _serverName,
                remoteUrl,
                tokens.AccessToken,
                expiresAt,
                tokens.RefreshToken,
                tokens.Scope,
                existing?.ClientId,
                existing?.ClientSecret,
                existing?.StepUpScope,
                existing?.DiscoveryState));

        _pendingStepUpScope = null;
    }

    public void PersistAuthorizationUrl(string authorizationUrl)
    {
        _authorizationUrl = authorizationUrl;
        if (!Uri.TryCreate(authorizationUrl, UriKind.Absolute, out var parsed))
        {
            return;
        }

        var query = ParseQuery(parsed.Query);
        if (query.TryGetValue("scope", out var scope) &&
            !string.IsNullOrWhiteSpace(scope))
        {
            _authStateService.PersistStepUpScope(_serverName, _serverConfig, scope);
        }
    }

    public void SaveCodeVerifier(string codeVerifier)
    {
        _codeVerifier = codeVerifier;
    }

    public string GetCodeVerifier()
    {
        if (string.IsNullOrEmpty(_codeVerifier))
        {
            throw new InvalidOperationException("No code verifier saved");
        }

        return _codeVerifier;
    }

    public void InvalidateCredentials(string scope)
    {
        if (string.Equals(scope, "verifier", StringComparison.Ordinal))
        {
            _codeVerifier = null;
            return;
        }

        _authStateService.InvalidateCredentials(_serverName, _serverConfig, scope);
    }

    public void SaveDiscoveryState(McpOAuthDiscoveryState state)
    {
        _authStateService.SaveDiscoveryState(_serverName, _serverConfig, state);
    }

    public McpOAuthDiscoveryState? DiscoveryState()
    {
        return _authStateService.GetDiscoveryState(_serverName, _serverConfig);
    }

    private static string GetRemoteUrl(McpServerConfig serverConfig)
    {
        return serverConfig switch
        {
            McpHttpServerConfig http => http.Url,
            McpSseServerConfig sse => sse.Url,
            _ => throw new ArgumentException(
                $"MCP OAuth provider only applies to HTTP and SSE servers (got: {serverConfig.Type}).",
                nameof(serverConfig))
        };
    }

    private static IReadOnlyDictionary<string, string> ParseQuery(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var trimmed = query[0] == '?' ? query[1..] : query;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator < 0)
            {
                result[Uri.UnescapeDataString(pair)] = string.Empty;
                continue;
            }

            var key = Uri.UnescapeDataString(pair[..separator]);
            var value = Uri.UnescapeDataString(pair[(separator + 1)..]);
            result[key] = value;
        }

        return result;
    }
}
