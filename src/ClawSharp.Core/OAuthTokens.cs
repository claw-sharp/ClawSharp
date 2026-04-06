namespace ClawSharp.Core;

/// <summary>
/// Represents OAuth tokens for Claude AI authentication.
/// TS origin: OAuthTokens in services/oauth/types.ts
/// </summary>
public record OAuthTokens(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<string> Scopes,
    string? SubscriptionType,
    string? RateLimitTier);
