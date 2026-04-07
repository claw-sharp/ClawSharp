namespace ClawSharp.Core;

/// <summary>
/// Represents OAuth tokens for Claude AI authentication.
/// </summary>
public record OAuthTokens(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<string> Scopes,
    string? SubscriptionType,
    string? RateLimitTier);
