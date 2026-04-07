using System.Text.Json;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class McpSdkHttpTransportFactory
{
    private const string DefaultClientMetadataDocumentUrl = "https://claude.ai/oauth/claude-code-client-metadata";

    private readonly IMcpSecureStorage _secureStorage;
    private readonly McpAuthStateService _authStateService;
    private readonly Func<Uri, CancellationToken, Task>? _openBrowserAsync;
    private readonly Func<Uri, CancellationToken, Task<string>> _metadataDocumentFetcherAsync;

    public McpSdkHttpTransportFactory(
        IMcpSecureStorage secureStorage,
        McpAuthStateService authStateService,
        Func<Uri, CancellationToken, Task>? openBrowserAsync = null)
        : this(
            secureStorage,
            authStateService,
            openBrowserAsync,
            FetchMetadataDocumentAsync)
    {
    }

    internal McpSdkHttpTransportFactory(
        IMcpSecureStorage secureStorage,
        McpAuthStateService authStateService,
        Func<Uri, CancellationToken, Task>? openBrowserAsync,
        Func<Uri, CancellationToken, Task<string>> metadataDocumentFetcherAsync)
    {
        _secureStorage = secureStorage;
        _authStateService = authStateService;
        _openBrowserAsync = openBrowserAsync;
        _metadataDocumentFetcherAsync = metadataDocumentFetcherAsync;
    }

    public async Task<HttpClientTransportOptions> CreateAsync(
        string serverName,
        ScopedMcpServerConfig scopedConfig,
        Action<string>? onAuthorizationUrl = null,
        bool skipBrowserOpen = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serverName);
        ArgumentNullException.ThrowIfNull(scopedConfig);

        var (endpoint, headers, oauthConfig, mode) = scopedConfig.Config switch
        {
            McpHttpServerConfig http => (
                new Uri(http.Url, UriKind.Absolute),
                http.Headers,
                http.OAuth,
                HttpTransportMode.StreamableHttp),
            McpSseServerConfig sse => (
                new Uri(sse.Url, UriKind.Absolute),
                sse.Headers,
                sse.OAuth,
                HttpTransportMode.Sse),
            _ => throw new ArgumentException(
                $"SDK HTTP transport only applies to HTTP and SSE servers (got: {scopedConfig.Type}).",
                nameof(scopedConfig))
        };

        var redirectPort = oauthConfig?.CallbackPort ?? McpOAuthPort.RedirectPortFallback;
        var redirectUri = new Uri(McpOAuthPort.BuildRedirectUri(redirectPort), UriKind.Absolute);
        var clientInfo = new McpOAuthClientProvider(serverName, scopedConfig.Config, _secureStorage, _authStateService)
            .ClientInformation();
        var clientMetadataUrl = GetClientMetadataDocumentUri();
        var tokenCache = new McpSdkTokenCache(serverName, scopedConfig.Config, _secureStorage, _authStateService);
        var redirectHandler = new McpSdkAuthorizationRedirectHandler(
            onAuthorizationUrl,
            _openBrowserAsync,
            skipBrowserOpen);
        var configuredAuthServerIssuer = await GetConfiguredAuthServerIssuerAsync(
            serverName,
            scopedConfig,
            oauthConfig,
            cancellationToken).ConfigureAwait(false);

        return new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            Name = serverName,
            TransportMode = mode,
            AdditionalHeaders = headers is null
                ? null
                : new Dictionary<string, string>(headers, StringComparer.Ordinal),
            OAuth = new ClientOAuthOptions
            {
                RedirectUri = redirectUri,
                ClientId = oauthConfig?.ClientId ?? clientInfo?.ClientId,
                ClientSecret = clientInfo?.ClientSecret,
                ClientMetadataDocumentUri = clientMetadataUrl,
                AuthServerSelector = configuredAuthServerIssuer is null
                    ? null
                    : availableServers => SelectConfiguredAuthServer(configuredAuthServerIssuer, availableServers),
                AuthorizationRedirectDelegate = redirectHandler.CreateDelegate(),
                DynamicClientRegistration = new DynamicClientRegistrationOptions
                {
                    ClientName = $"ClawSharp ({serverName})",
                    ResponseDelegate = async (response, cancellationToken) =>
                    {
                        var provider = new McpOAuthClientProvider(
                            serverName,
                            scopedConfig.Config,
                            _secureStorage,
                            _authStateService);

                        provider.SaveClientInformation(
                            new McpOAuthClientInformation(
                                response.ClientId,
                                response.ClientSecret));

                        await Task.CompletedTask.ConfigureAwait(false);
                    }
                },
                TokenCache = tokenCache
            }
        };
    }

    private async Task<Uri?> GetConfiguredAuthServerIssuerAsync(
        string serverName,
        ScopedMcpServerConfig scopedConfig,
        McpOAuthConfig? oauthConfig,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(oauthConfig?.AuthServerMetadataUrl))
        {
            return null;
        }

        var configuredMetadataUri = new Uri(oauthConfig.AuthServerMetadataUrl, UriKind.Absolute);
        if (!string.Equals(configuredMetadataUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"authServerMetadataUrl must use https:// (got: {oauthConfig.AuthServerMetadataUrl})");
        }

        var responseBody = await _metadataDocumentFetcherAsync(configuredMetadataUri, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty("issuer", out var issuerElement) ||
            issuerElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(issuerElement.GetString()))
        {
            throw new InvalidOperationException(
                $"Configured auth server metadata at {oauthConfig.AuthServerMetadataUrl} did not contain a valid issuer.");
        }

        var issuer = new Uri(issuerElement.GetString()!, UriKind.Absolute);
        _authStateService.SaveDiscoveryState(
            serverName,
            scopedConfig.Config,
            new McpOAuthDiscoveryState(issuer.AbsoluteUri));
        return issuer;
    }

    private static Uri? SelectConfiguredAuthServer(Uri configuredAuthServerIssuer, IReadOnlyList<Uri> availableServers)
    {
        foreach (var availableServer in availableServers)
        {
            if (Uri.Compare(
                    availableServer,
                    configuredAuthServerIssuer,
                    UriComponents.AbsoluteUri,
                    UriFormat.SafeUnescaped,
                    StringComparison.OrdinalIgnoreCase) == 0)
            {
                return availableServer;
            }
        }

        return null;
    }

    private static Uri GetClientMetadataDocumentUri()
    {
        var configured = Environment.GetEnvironmentVariable("MCP_OAUTH_CLIENT_METADATA_URL");
        var value = string.IsNullOrWhiteSpace(configured)
            ? DefaultClientMetadataDocumentUrl
            : configured;

        return new Uri(value, UriKind.Absolute);
    }

    private static async Task<string> FetchMetadataDocumentAsync(Uri metadataUri, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, metadataUri);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"HTTP {(int)response.StatusCode} fetching configured auth server metadata from {metadataUri}");
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }
}
