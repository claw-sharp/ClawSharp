// TS origin: ./services/api/filesApi.ts, ./main.tsx, ./utils/envUtils.ts
// TS parity status: ports the direct env-backed query-model HTTP client config foundation for `ANTHROPIC_BASE_URL`, `ANTHROPIC_API_KEY`, first-party auth-token env fallbacks, and persisted Claude AI OAuth-token fallback; apiKeyHelper, file-descriptor token loading, and third-party provider auth remain intentionally unported.
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.Infrastructure;

public sealed class EnvironmentQueryModelHttpClientConfigProvider : IQueryModelHttpClientConfigProvider
{
    public const string DefaultBaseUrl = "https://api.anthropic.com";
    private readonly IMcpSecureStorage? _secureStorage;
    private readonly ClaudeAiOAuthTokenSource _oauthTokenSource;

    public EnvironmentQueryModelHttpClientConfigProvider(
        IMcpSecureStorage? secureStorage = null,
        ClaudeAiOAuthTokenSource? oauthTokenSource = null)
    {
        _secureStorage = secureStorage;
        _oauthTokenSource = oauthTokenSource ?? new ClaudeAiOAuthTokenSource();
    }

    public QueryModelHttpClientConfig GetConfig(
        QueryTurnRequest request,
        QueryLoopState state,
        ConversationSession session,
        ClawSharpSettings settings)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(settings);

        var baseUrl = Environment.GetEnvironmentVariable("ANTHROPIC_BASE_URL");
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = settings.ClaudeApiKey;
        }

        var authToken = string.IsNullOrWhiteSpace(apiKey)
            ? ResolveAuthToken()
            : null;

        return new QueryModelHttpClientConfig(
            string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl,
            string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
            authToken);
    }

    private string? ResolveAuthToken()
    {
        var anthropicAuthToken = Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");
        if (!string.IsNullOrWhiteSpace(anthropicAuthToken))
        {
            return anthropicAuthToken;
        }

        var claudeCodeOauthToken = Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN");
        if (!string.IsNullOrWhiteSpace(claudeCodeOauthToken))
        {
            return claudeCodeOauthToken;
        }

        var fileDescriptorToken = _oauthTokenSource.ReadToken();
        if (!string.IsNullOrWhiteSpace(fileDescriptorToken))
        {
            return fileDescriptorToken;
        }

        return _secureStorage?.Read()?.ClaudeAiOauth?.AccessToken;
    }
}
