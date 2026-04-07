// TS parity status: ports the direct env-backed query-model HTTP client config foundation for `ANTHROPIC_BASE_URL`, `ANTHROPIC_API_KEY`, first-party auth-token env fallbacks, and persisted Claude AI OAuth-token fallback; apiKeyHelper, file-descriptor token loading, and third-party provider auth remain intentionally unported.
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.Infrastructure;

public sealed class EnvironmentQueryModelHttpClientConfigProvider : IQueryModelHttpClientConfigProvider
{
    public const string DefaultBaseUrl = ProviderRuntimeResolver.DefaultAnthropicBaseUrl;
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

        var effectiveModel =
            state.ToolUseContext.MainLoopModel ??
            settings.Runtime.Model;
        var runtimeConfig = ProviderRuntimeResolver.Resolve(settings, effectiveModel);

        if (runtimeConfig.Provider is ApiProviderKind.Anthropic or ApiProviderKind.Bedrock or ApiProviderKind.Vertex or ApiProviderKind.Foundry)
        {
            var apiKey = runtimeConfig.ApiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = settings.ClaudeApiKey;
            }

            var authToken = string.IsNullOrWhiteSpace(apiKey)
                ? runtimeConfig.AuthToken ?? ResolveAuthToken()
                : null;

            return new QueryModelHttpClientConfig(
                runtimeConfig.BaseUrl,
                string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
                authToken,
                runtimeConfig.Transport,
                runtimeConfig.Provider,
                runtimeConfig.AccountId,
                runtimeConfig.ApiVersion,
                runtimeConfig.AdditionalHeaders);
        }

        return new QueryModelHttpClientConfig(
            runtimeConfig.BaseUrl,
            runtimeConfig.ApiKey,
            runtimeConfig.AuthToken,
            runtimeConfig.Transport,
            runtimeConfig.Provider,
            runtimeConfig.AccountId,
            runtimeConfig.ApiVersion,
            runtimeConfig.AdditionalHeaders);
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
