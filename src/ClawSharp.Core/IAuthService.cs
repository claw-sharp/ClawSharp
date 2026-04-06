namespace ClawSharp.Core;

/// <summary>
/// Service to manage authentication and user state.
/// TS origin: utils/auth.ts
/// </summary>
public interface IAuthService
{
    bool IsAnthropicAuthEnabled();
    bool IsClaudeAISubscriber();
    bool IsUsing3PServices();
    bool IsFirstPartyAnthropicBaseUrl();
    OAuthTokens? GetClaudeAIOAuthTokens();
}
