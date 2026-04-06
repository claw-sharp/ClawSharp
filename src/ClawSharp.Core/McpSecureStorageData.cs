// TS origin: ./utils/secureStorage/types.ts
using System.Text.Json.Serialization;

namespace ClawSharp.Core;

public sealed record McpSecureStorageData(
    [property: JsonPropertyName("claudeAiOauth")]
    ClaudeAiOAuthEntry? ClaudeAiOauth = null,
    [property: JsonPropertyName("trustedDeviceToken")]
    string? TrustedDeviceToken = null,
    [property: JsonPropertyName("mcpOAuth")]
    IReadOnlyDictionary<string, McpOAuthEntry>? McpOAuth = null,
    [property: JsonPropertyName("mcpOAuthClientConfig")]
    IReadOnlyDictionary<string, McpOAuthClientConfigEntry>? McpOAuthClientConfig = null,
    [property: JsonPropertyName("pluginSecrets")]
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? PluginSecrets = null);

public sealed record ClaudeAiOAuthEntry(
    [property: JsonPropertyName("accessToken")]
    string? AccessToken,
    [property: JsonPropertyName("refreshToken")]
    string? RefreshToken,
    [property: JsonPropertyName("expiresAt")]
    long? ExpiresAt,
    [property: JsonPropertyName("scopes")]
    IReadOnlyList<string>? Scopes = null,
    [property: JsonPropertyName("subscriptionType")]
    string? SubscriptionType = null,
    [property: JsonPropertyName("rateLimitTier")]
    string? RateLimitTier = null);

public sealed record McpOAuthEntry(
    [property: JsonPropertyName("serverName")]
    string ServerName,
    [property: JsonPropertyName("serverUrl")]
    string ServerUrl,
    [property: JsonPropertyName("accessToken")]
    string? AccessToken,
    [property: JsonPropertyName("expiresAt")]
    long ExpiresAt,
    [property: JsonPropertyName("refreshToken")]
    string? RefreshToken = null,
    [property: JsonPropertyName("scope")]
    string? Scope = null,
    [property: JsonPropertyName("clientId")]
    string? ClientId = null,
    [property: JsonPropertyName("clientSecret")]
    string? ClientSecret = null,
    [property: JsonPropertyName("stepUpScope")]
    string? StepUpScope = null,
    [property: JsonPropertyName("discoveryState")]
    McpOAuthDiscoveryState? DiscoveryState = null);

public sealed record McpOAuthDiscoveryState(
    [property: JsonPropertyName("authorizationServerUrl")]
    string? AuthorizationServerUrl = null,
    [property: JsonPropertyName("resourceMetadataUrl")]
    string? ResourceMetadataUrl = null);

public sealed record McpOAuthClientConfigEntry(
    [property: JsonPropertyName("clientSecret")]
    string? ClientSecret);
