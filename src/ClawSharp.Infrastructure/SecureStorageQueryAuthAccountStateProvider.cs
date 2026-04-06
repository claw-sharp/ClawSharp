// TS parity status: ports the direct C#-equivalent Claude AI OAuth account-state lookup from persisted secure-storage token metadata plus env-token fallbacks; file-descriptor token loading and profile refresh remain intentionally unported.
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.Infrastructure;

public sealed class SecureStorageQueryAuthAccountStateProvider : IQueryAuthAccountStateProvider
{
    private const string ClaudeAiInferenceScope = "user:inference";

    private readonly IMcpSecureStorage _secureStorage;
    private readonly ClaudeAiOAuthTokenSource _oauthTokenSource;
    private readonly Func<string, string?> _getEnvironmentVariable;

    public SecureStorageQueryAuthAccountStateProvider(
        IMcpSecureStorage secureStorage,
        ClaudeAiOAuthTokenSource? oauthTokenSource = null,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        _secureStorage = secureStorage;
        _getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        _oauthTokenSource = oauthTokenSource ?? new ClaudeAiOAuthTokenSource(getEnvironmentVariable: _getEnvironmentVariable);
    }

    public QueryAuthAccountState GetState(
        QueryTurnRequest request,
        QueryLoopState state,
        ConversationSession session,
        ClawSharpSettings settings)
    {
        if (!IsAnthropicAuthEnabled())
        {
            return QueryAuthAccountState.None;
        }

        if (!string.IsNullOrWhiteSpace(_getEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN")))
        {
            return new QueryAuthAccountState(
                IsClaudeAiSubscriber: true,
                IsEnterpriseSubscriber: false);
        }

        if (!string.IsNullOrWhiteSpace(_oauthTokenSource.ReadToken()))
        {
            return new QueryAuthAccountState(
                IsClaudeAiSubscriber: true,
                IsEnterpriseSubscriber: false);
        }

        var oauth = _secureStorage.Read()?.ClaudeAiOauth;
        if (oauth is null ||
            string.IsNullOrWhiteSpace(oauth.AccessToken))
        {
            return QueryAuthAccountState.None;
        }

        var isSubscriber = oauth.Scopes?.Contains(ClaudeAiInferenceScope, StringComparer.Ordinal) == true;
        var isEnterprise = string.Equals(oauth.SubscriptionType, "enterprise", StringComparison.Ordinal);
        return new QueryAuthAccountState(
            IsClaudeAiSubscriber: isSubscriber,
            IsEnterpriseSubscriber: isEnterprise,
            oauth.SubscriptionType,
            oauth.RateLimitTier);
    }

    private bool IsAnthropicAuthEnabled()
    {
        return !IsTruthy(_getEnvironmentVariable("CLAUDE_CODE_USE_BEDROCK")) &&
               !IsTruthy(_getEnvironmentVariable("CLAUDE_CODE_USE_VERTEX")) &&
               !IsTruthy(_getEnvironmentVariable("CLAUDE_CODE_USE_FOUNDRY")) &&
               string.IsNullOrWhiteSpace(_getEnvironmentVariable("ANTHROPIC_API_KEY")) &&
               string.IsNullOrWhiteSpace(_getEnvironmentVariable("ANTHROPIC_AUTH_TOKEN"));
    }

    private static bool IsTruthy(string? value)
    {
        return string.Equals(value, "1", StringComparison.Ordinal) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }
}
