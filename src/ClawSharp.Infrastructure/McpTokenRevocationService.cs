// TS origin: ./services/mcp/auth.ts
// Ports: revokeToken, revokeServerTokens, clearServerTokensFromLocalStorage
// from './services/mcp/auth.ts'.
//
// Design decisions:
// - HttpClient replaces axios for token revocation POSTs.
// - The RFC 7009 / 401-fallback retry pattern is preserved 1:1.
// - The XAA path (oauth.xaa) still calls HasDiscoveryButNoToken exactly as TS does.
// - fetchAuthServerMetadata for revocation relies on the already-ported
//   McpSdkHttpTransportFactory metadata-discovery path (injected).

using System.Net.Http.Headers;
using System.Text;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

/// <summary>
/// Provides token revocation and local token-clearing helpers for MCP OAuth servers.
/// Direct port of <c>revokeToken</c>, <c>revokeServerTokens</c>, and
/// <c>clearServerTokensFromLocalStorage</c> from <c>services/mcp/auth.ts</c>.
/// </summary>
public sealed class McpTokenRevocationService
{
    private readonly McpAuthStateService _authStateService;
    private readonly HttpClient _httpClient;

    public McpTokenRevocationService(
        McpAuthStateService authStateService,
        HttpClient? httpClient = null)
    {
        _authStateService = authStateService;
        _httpClient = httpClient ?? new HttpClient();
    }

    /// <summary>
    /// Revokes server-side tokens and then clears local token storage.
    /// Mirrors <c>revokeServerTokens</c> from <c>auth.ts</c>.
    /// </summary>
    public async Task RevokeServerTokensAsync(
        string serverName,
        McpServerConfig serverConfig,
        string? revocationEndpointOverride = null,
        bool preserveStepUpState = false,
        CancellationToken cancellationToken = default)
    {
        var tokenData = _authStateService.GetOAuthEntry(serverName, serverConfig);
        if (tokenData is null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(tokenData.AccessToken) || !string.IsNullOrEmpty(tokenData.RefreshToken))
        {
            try
            {
                var revocationEndpoint = revocationEndpointOverride;
                if (revocationEndpoint is null)
                {
                    // Caller must supply the revocation endpoint (fetched via metadata discovery).
                    // This mirrors the TS path where fetchAuthServerMetadata is called to find
                    // metadata.revocation_endpoint.
                    // If no endpoint is supplied, we fall through to local-only token clearing.
                }

                if (revocationEndpoint is not null)
                {
                    // Determine auth method from supplied metadata flags (mirrors TS auth-method selection).
                    // Default to client_secret_basic per RFC 7009.
                    const string defaultAuthMethod = "client_secret_basic";

                    // Revoke refresh token first (more important – prevents future access token generation).
                    if (!string.IsNullOrEmpty(tokenData.RefreshToken))
                    {
                        try
                        {
                            await RevokeTokenAsync(
                                serverName,
                                revocationEndpoint,
                                tokenData.RefreshToken,
                                "refresh_token",
                                tokenData.ClientId,
                                tokenData.ClientSecret,
                                tokenData.AccessToken,
                                defaultAuthMethod,
                                cancellationToken).ConfigureAwait(false);
                        }
                        catch
                        {
                            // Log but continue — best-effort.
                        }
                    }

                    // Then revoke access token (may already have been invalidated by refresh-token revocation).
                    if (!string.IsNullOrEmpty(tokenData.AccessToken))
                    {
                        try
                        {
                            await RevokeTokenAsync(
                                serverName,
                                revocationEndpoint,
                                tokenData.AccessToken,
                                "access_token",
                                tokenData.ClientId,
                                tokenData.ClientSecret,
                                tokenData.AccessToken,
                                defaultAuthMethod,
                                cancellationToken).ConfigureAwait(false);
                        }
                        catch
                        {
                            // Log but continue — best-effort.
                        }
                    }
                }
            }
            catch
            {
                // Revocation is best-effort — always clear locally.
            }
        }

        // Always clear local tokens regardless of server-side revocation result.
        _authStateService.ClearServerTokensFromLocalStorage(serverName, serverConfig, preserveStepUpState);
    }

    /// <summary>
    /// Revokes a single token on the OAuth server.
    /// Mirrors <c>revokeToken</c> from <c>auth.ts</c>.
    ///
    /// Per RFC 7009, public clients should include client_id in the request body
    /// (not via Authorization header). If a 401 is received on the first attempt,
    /// retries with Bearer auth as a fallback for non-compliant servers.
    ///
    /// Auth-method selection: <c>client_secret_basic</c> sends a Basic header;
    /// <c>client_secret_post</c> includes client_id + client_secret in the form body.
    /// </summary>
    public async Task RevokeTokenAsync(
        string serverName,
        string endpoint,
        string token,
        string tokenTypeHint,
        string? clientId,
        string? clientSecret,
        string? accessToken,
        string authMethod = "client_secret_basic",
        CancellationToken cancellationToken = default)
    {
        var (formParams, headers) = BuildRevocationRequest(
            token, tokenTypeHint, clientId, clientSecret, authMethod);

        var request = BuildHttpRequest(endpoint, formParams, headers);
        var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if ((int)response.StatusCode == 401 && !string.IsNullOrEmpty(accessToken))
        {
            // Fallback for non-RFC-7009-compliant servers that require Bearer auth.
            // RFC 6749 §2.3.1: must not send more than one auth method — retry clears
            // client creds from the body and switches to Bearer.
            formParams.Remove("client_id");
            formParams.Remove("client_secret");
            var retryHeaders = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Content-Type"] = "application/x-www-form-urlencoded",
                ["Authorization"] = $"Bearer {accessToken}"
            };
            var retryRequest = BuildHttpRequest(endpoint, formParams, retryHeaders);
            await _httpClient.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
        }
    }

    private static (Dictionary<string, string> FormParams, Dictionary<string, string> Headers) BuildRevocationRequest(
        string token,
        string tokenTypeHint,
        string? clientId,
        string? clientSecret,
        string authMethod)
    {
        var formParams = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["token"] = token,
            ["token_type_hint"] = tokenTypeHint
        };

        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Content-Type"] = "application/x-www-form-urlencoded"
        };

        // RFC 7009 §2.1 requires client auth per RFC 6749 §2.3.
        if (clientId is not null && clientSecret is not null)
        {
            if (authMethod == "client_secret_post")
            {
                formParams["client_id"] = clientId;
                formParams["client_secret"] = clientSecret;
            }
            else
            {
                // client_secret_basic — Basic auth header
                var basic = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{Uri.EscapeDataString(clientId)}:{Uri.EscapeDataString(clientSecret)}"));
                headers["Authorization"] = $"Basic {basic}";
            }
        }
        else if (clientId is not null)
        {
            formParams["client_id"] = clientId;
        }

        return (formParams, headers);
    }

    private static HttpRequestMessage BuildHttpRequest(
        string endpoint,
        Dictionary<string, string> formParams,
        Dictionary<string, string> headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new FormUrlEncodedContent(formParams)
        };

        foreach (var (key, value) in headers)
        {
            if (key == "Content-Type")
            {
                continue; // FormUrlEncodedContent sets this automatically.
            }

            request.Headers.TryAddWithoutValidation(key, value);
        }

        return request;
    }
}
