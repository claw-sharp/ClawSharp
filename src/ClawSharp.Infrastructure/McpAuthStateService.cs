using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class McpAuthStateService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    private readonly IMcpSecureStorage _secureStorage;

    public McpAuthStateService(IMcpSecureStorage secureStorage)
    {
        _secureStorage = secureStorage;
    }

    public static string GetServerKey(string serverName, McpServerConfig serverConfig)
    {
        var remoteConfig = GetRemoteConfig(serverConfig);
        var configJson = JsonSerializer.Serialize(
            new
            {
                type = remoteConfig.Type,
                url = remoteConfig.Url,
                headers = remoteConfig.Headers ?? new Dictionary<string, string>(StringComparer.Ordinal)
            },
            SerializerOptions);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(configJson));
        var hash = Convert.ToHexString(bytes).ToLowerInvariant()[..16];
        return $"{serverName}|{hash}";
    }

    public bool HasDiscoveryButNoToken(string serverName, McpServerConfig serverConfig)
    {
        var remoteConfig = GetRemoteConfig(serverConfig);
        if (remoteConfig.OAuth?.Xaa == true)
        {
            return false;
        }

        var serverKey = GetServerKey(serverName, serverConfig);
        var entry = _secureStorage.Read()?.McpOAuth?.GetValueOrDefault(serverKey);
        return entry is not null &&
               string.IsNullOrEmpty(entry.AccessToken) &&
               string.IsNullOrEmpty(entry.RefreshToken);
    }

    public McpOAuthEntry? GetOAuthEntry(string serverName, McpServerConfig serverConfig)
    {
        var serverKey = GetServerKey(serverName, serverConfig);
        return _secureStorage.Read()?.McpOAuth?.GetValueOrDefault(serverKey);
    }

    public void SaveOAuthEntry(string serverName, McpServerConfig serverConfig, McpOAuthEntry entry)
    {
        var existingData = _secureStorage.Read() ?? new McpSecureStorageData();
        var serverKey = GetServerKey(serverName, serverConfig);
        var oauth = existingData.McpOAuth is null
            ? new Dictionary<string, McpOAuthEntry>(StringComparer.Ordinal)
            : new Dictionary<string, McpOAuthEntry>(existingData.McpOAuth, StringComparer.Ordinal);

        oauth[serverKey] = entry;
        _secureStorage.Update(existingData with { McpOAuth = oauth });
    }

    public void ClearServerTokensFromLocalStorage(
        string serverName,
        McpServerConfig serverConfig,
        bool preserveStepUpState = false)
    {
        var existingData = _secureStorage.Read();
        if (existingData?.McpOAuth is null)
        {
            return;
        }

        var serverKey = GetServerKey(serverName, serverConfig);
        var existingEntry = existingData.McpOAuth.GetValueOrDefault(serverKey);
        if (existingEntry is null)
        {
            return;
        }

        var oauth = new Dictionary<string, McpOAuthEntry>(existingData.McpOAuth, StringComparer.Ordinal);
        if (!oauth.Remove(serverKey))
        {
            return;
        }

        if (preserveStepUpState &&
            (existingEntry.StepUpScope is not null || existingEntry.DiscoveryState is not null))
        {
            oauth[serverKey] = new McpOAuthEntry(
                serverName,
                GetRemoteConfig(serverConfig).Url,
                oauth.GetValueOrDefault(serverKey)?.AccessToken ?? string.Empty,
                oauth.GetValueOrDefault(serverKey)?.ExpiresAt ?? 0,
                StepUpScope: existingEntry.StepUpScope,
                DiscoveryState: existingEntry.DiscoveryState is null
                    ? null
                    : new McpOAuthDiscoveryState(
                        existingEntry.DiscoveryState.AuthorizationServerUrl,
                        existingEntry.DiscoveryState.ResourceMetadataUrl));
        }

        _secureStorage.Update(existingData with { McpOAuth = oauth });
    }

    public void SaveDiscoveryState(string serverName, McpServerConfig serverConfig, McpOAuthDiscoveryState state)
    {
        var existingData = _secureStorage.Read() ?? new McpSecureStorageData();
        var serverKey = GetServerKey(serverName, serverConfig);
        var existingEntry = existingData.McpOAuth?.GetValueOrDefault(serverKey);
        var oauth = existingData.McpOAuth is null
            ? new Dictionary<string, McpOAuthEntry>(StringComparer.Ordinal)
            : new Dictionary<string, McpOAuthEntry>(existingData.McpOAuth, StringComparer.Ordinal);

        oauth[serverKey] = new McpOAuthEntry(
            serverName,
            GetRemoteConfig(serverConfig).Url,
            existingEntry?.AccessToken ?? string.Empty,
            existingEntry?.ExpiresAt ?? 0,
            existingEntry?.RefreshToken,
            existingEntry?.Scope,
            existingEntry?.ClientId,
            existingEntry?.ClientSecret,
            existingEntry?.StepUpScope,
            new McpOAuthDiscoveryState(
                state.AuthorizationServerUrl,
                state.ResourceMetadataUrl));

        _secureStorage.Update(existingData with { McpOAuth = oauth });
    }

    public McpOAuthDiscoveryState? GetDiscoveryState(string serverName, McpServerConfig serverConfig)
    {
        var serverKey = GetServerKey(serverName, serverConfig);
        return _secureStorage.Read()?.McpOAuth?.GetValueOrDefault(serverKey)?.DiscoveryState;
    }

    public void PersistStepUpScope(string serverName, McpServerConfig serverConfig, string stepUpScope)
    {
        var existingData = _secureStorage.Read();
        if (existingData?.McpOAuth is null)
        {
            return;
        }

        var serverKey = GetServerKey(serverName, serverConfig);
        var existingEntry = existingData.McpOAuth.GetValueOrDefault(serverKey);
        if (existingEntry is null)
        {
            return;
        }

        var oauth = new Dictionary<string, McpOAuthEntry>(existingData.McpOAuth, StringComparer.Ordinal)
        {
            [serverKey] = existingEntry with { StepUpScope = stepUpScope }
        };

        _secureStorage.Update(existingData with { McpOAuth = oauth });
    }

    public void InvalidateCredentials(string serverName, McpServerConfig serverConfig, string scope)
    {
        var existingData = _secureStorage.Read();
        if (existingData?.McpOAuth is null)
        {
            return;
        }

        var serverKey = GetServerKey(serverName, serverConfig);
        var existingEntry = existingData.McpOAuth.GetValueOrDefault(serverKey);
        if (existingEntry is null)
        {
            return;
        }

        var oauth = new Dictionary<string, McpOAuthEntry>(existingData.McpOAuth, StringComparer.Ordinal);

        switch (scope)
        {
            case "all":
                oauth.Remove(serverKey);
                break;
            case "client":
                oauth[serverKey] = existingEntry with
                {
                    ClientId = null,
                    ClientSecret = null
                };
                break;
            case "tokens":
                oauth[serverKey] = existingEntry with
                {
                    AccessToken = string.Empty,
                    RefreshToken = null,
                    ExpiresAt = 0
                };
                break;
            case "discovery":
                oauth[serverKey] = existingEntry with
                {
                    DiscoveryState = null,
                    StepUpScope = null
                };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unsupported credential invalidation scope.");
        }

        _secureStorage.Update(existingData with { McpOAuth = oauth });
    }

    public void ClearStoredClientRegistration(string serverName, McpServerConfig serverConfig)
    {
        InvalidateCredentials(serverName, serverConfig, "client");
    }

    public void SaveMcpClientSecret(string serverName, McpServerConfig serverConfig, string clientSecret)
    {
        var existingData = _secureStorage.Read() ?? new McpSecureStorageData();
        var serverKey = GetServerKey(serverName, serverConfig);
        var clientConfig = existingData.McpOAuthClientConfig is null
            ? new Dictionary<string, McpOAuthClientConfigEntry>(StringComparer.Ordinal)
            : new Dictionary<string, McpOAuthClientConfigEntry>(existingData.McpOAuthClientConfig, StringComparer.Ordinal);

        clientConfig[serverKey] = new McpOAuthClientConfigEntry(clientSecret);
        _secureStorage.Update(existingData with { McpOAuthClientConfig = clientConfig });
    }

    public void ClearMcpClientConfig(string serverName, McpServerConfig serverConfig)
    {
        var existingData = _secureStorage.Read();
        if (existingData?.McpOAuthClientConfig is null)
        {
            return;
        }

        var serverKey = GetServerKey(serverName, serverConfig);
        var clientConfig = new Dictionary<string, McpOAuthClientConfigEntry>(existingData.McpOAuthClientConfig, StringComparer.Ordinal);
        if (!clientConfig.Remove(serverKey))
        {
            return;
        }

        _secureStorage.Update(existingData with { McpOAuthClientConfig = clientConfig });
    }

    public McpOAuthClientConfigEntry? GetMcpClientConfig(string serverName, McpServerConfig serverConfig)
    {
        var serverKey = GetServerKey(serverName, serverConfig);
        return _secureStorage.Read()?.McpOAuthClientConfig?.GetValueOrDefault(serverKey);
    }

    private static RemoteServerConfig GetRemoteConfig(McpServerConfig serverConfig)
    {
        return serverConfig switch
        {
            McpSseServerConfig sse => new RemoteServerConfig(sse.Type, sse.Url, sse.Headers, sse.OAuth),
            McpHttpServerConfig http => new RemoteServerConfig(http.Type, http.Url, http.Headers, http.OAuth),
            _ => throw new ArgumentException(
                $"MCP auth state only applies to HTTP and SSE servers (got: {serverConfig.Type}).",
                nameof(serverConfig))
        };
    }

    private sealed record RemoteServerConfig(
        string Type,
        string Url,
        IReadOnlyDictionary<string, string>? Headers,
        McpOAuthConfig? OAuth);
}
