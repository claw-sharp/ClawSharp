// TS origin: ./services/mcp/auth.ts
using ModelContextProtocol.Authentication;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class McpSdkTokenCache : ITokenCache
{
    private readonly string _serverName;
    private readonly McpServerConfig _serverConfig;
    private readonly McpOAuthClientProvider _provider;

    public McpSdkTokenCache(
        string serverName,
        McpServerConfig serverConfig,
        IMcpSecureStorage secureStorage,
        McpAuthStateService authStateService)
    {
        _serverName = serverName;
        _serverConfig = serverConfig;
        _provider = new McpOAuthClientProvider(serverName, serverConfig, secureStorage, authStateService);
    }

    public async ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        cancellationToken.ThrowIfCancellationRequested();

        _provider.SaveTokens(
            new McpOAuthTokens(
                tokens.AccessToken,
                tokens.RefreshToken,
                tokens.ExpiresIn,
                tokens.Scope));

        await ValueTask.CompletedTask.ConfigureAwait(false);
    }

    public async ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken)
    {
        var tokens = await _provider.TokensAsync(cancellationToken).ConfigureAwait(false);
        if (tokens is null)
        {
            return null;
        }

        return new TokenContainer
        {
            AccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken,
            ExpiresIn = tokens.ExpiresIn is null
                ? null
                : Convert.ToInt32(Math.Floor(tokens.ExpiresIn.Value)),
            Scope = tokens.Scope,
            TokenType = "Bearer",
            ObtainedAt = DateTimeOffset.UtcNow
        };
    }
}
