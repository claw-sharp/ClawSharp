// TS origin: ./services/api/filesApi.ts, ./main.tsx, ./utils/envUtils.ts
// TS parity status: focused coverage for the env-backed query-model HTTP client config provider foundation, including first-party auth-token fallbacks from env and persisted Claude AI OAuth state.
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public sealed class EnvironmentQueryModelHttpClientConfigProviderTests
{
    [Fact]
    public void GetConfig_Uses_Ts_Default_BaseUrl_When_Env_Is_Unset()
    {
        var previousBaseUrl = Environment.GetEnvironmentVariable("ANTHROPIC_BASE_URL");
        var previousApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        var previousAuthToken = Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");
        var previousOauthToken = Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_BASE_URL", null);
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
            Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", null);
            Environment.SetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN", null);
            var provider = new EnvironmentQueryModelHttpClientConfigProvider();

            var config = provider.GetConfig(
                CreateRequest(),
                QueryLoopStateFactory.CreateInitial([]),
                CreateSession(),
                new ClawSharpSettings());

            Assert.Equal(EnvironmentQueryModelHttpClientConfigProvider.DefaultBaseUrl, config.BaseUrl);
            Assert.Null(config.ApiKey);
            Assert.Null(config.AuthToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_BASE_URL", previousBaseUrl);
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", previousApiKey);
            Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", previousAuthToken);
            Environment.SetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN", previousOauthToken);
        }
    }

    [Fact]
    public void GetConfig_Uses_Anthropic_Env_Overrides()
    {
        var previousBaseUrl = Environment.GetEnvironmentVariable("ANTHROPIC_BASE_URL");
        var previousApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        var previousAuthToken = Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_BASE_URL", "https://proxy.example.test");
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "env-key");
            Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", "ignored-auth-token");
            var provider = new EnvironmentQueryModelHttpClientConfigProvider();

            var config = provider.GetConfig(
                CreateRequest(),
                QueryLoopStateFactory.CreateInitial([]),
                CreateSession(),
                new ClawSharpSettings());

            Assert.Equal("https://proxy.example.test", config.BaseUrl);
            Assert.Equal("env-key", config.ApiKey);
            Assert.Null(config.AuthToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_BASE_URL", previousBaseUrl);
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", previousApiKey);
            Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", previousAuthToken);
        }
    }

    [Fact]
    public void GetConfig_Uses_Anthropic_Auth_Token_When_Api_Key_Is_Unset()
    {
        var previousApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        var previousAuthToken = Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
            Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", "anthropic-auth-token");
            var provider = new EnvironmentQueryModelHttpClientConfigProvider();

            var config = provider.GetConfig(
                CreateRequest(),
                QueryLoopStateFactory.CreateInitial([]),
                CreateSession(),
                new ClawSharpSettings());

            Assert.Null(config.ApiKey);
            Assert.Equal("anthropic-auth-token", config.AuthToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", previousApiKey);
            Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", previousAuthToken);
        }
    }

    [Fact]
    public void GetConfig_Uses_Stored_Claude_Api_Key_When_Env_Key_Is_Unset()
    {
        var previousApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        var previousAuthToken = Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
            Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", null);
            var provider = new EnvironmentQueryModelHttpClientConfigProvider();

            var config = provider.GetConfig(
                CreateRequest(),
                QueryLoopStateFactory.CreateInitial([]),
                CreateSession(),
                new ClawSharpSettings
                {
                    ClaudeApiKey = "stored-claude-key"
                });

            Assert.Equal("stored-claude-key", config.ApiKey);
            Assert.Null(config.AuthToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", previousApiKey);
            Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", previousAuthToken);
        }
    }

    [Fact]
    public void GetConfig_Falls_Back_To_Persisted_Claude_Ai_OAuth_Token()
    {
        var previousApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        var previousAuthToken = Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");
        var previousOauthToken = Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
            Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", null);
            Environment.SetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN", null);
            var provider = new EnvironmentQueryModelHttpClientConfigProvider(
                new InMemorySecureStorage(
                    new McpSecureStorageData(
                        ClaudeAiOauth: new ClaudeAiOAuthEntry(
                            AccessToken: "persisted-oauth-token",
                            RefreshToken: "refresh-token",
                            ExpiresAt: 123))));

            var config = provider.GetConfig(
                CreateRequest(),
                QueryLoopStateFactory.CreateInitial([]),
                CreateSession(),
                new ClawSharpSettings());

            Assert.Null(config.ApiKey);
            Assert.Equal("persisted-oauth-token", config.AuthToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", previousApiKey);
            Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", previousAuthToken);
            Environment.SetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN", previousOauthToken);
        }
    }

    [Fact]
    public void GetConfig_Uses_FileDescriptor_OAuth_Token_Fallback()
    {
        var previousApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        var previousAuthToken = Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");
        var previousOauthToken = Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN");
        var previousFd = Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR");
        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
            Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", null);
            Environment.SetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN", null);
            Environment.SetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR", null);
            var tokenFile = Path.Combine(Path.GetTempPath(), "clawsharp-model-http-config-provider-tests", Guid.NewGuid().ToString("N"), ".oauth_token");
            Directory.CreateDirectory(Path.GetDirectoryName(tokenFile)!);
            File.WriteAllText(tokenFile, "fd-oauth-token");
            var provider = new EnvironmentQueryModelHttpClientConfigProvider(
                oauthTokenSource: new ClaudeAiOAuthTokenSource(tokenFile));

            var config = provider.GetConfig(
                CreateRequest(),
                QueryLoopStateFactory.CreateInitial([]),
                CreateSession(),
                new ClawSharpSettings());

            Assert.Null(config.ApiKey);
            Assert.Equal("fd-oauth-token", config.AuthToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", previousApiKey);
            Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", previousAuthToken);
            Environment.SetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN", previousOauthToken);
            Environment.SetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR", previousFd);
        }
    }

    private static QueryTurnRequest CreateRequest()
    {
        return QueryTurnRequest.Create(CreateSession(), "hello");
    }

    private static ConversationSession CreateSession()
    {
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-model-http-config-provider-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        return new ConversationSession("session-config-provider", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
    }

    private sealed class InMemorySecureStorage : IMcpSecureStorage
    {
        private readonly McpSecureStorageData? _data;

        public InMemorySecureStorage(McpSecureStorageData? data = null)
        {
            _data = data;
        }

        public McpSecureStorageData? Read() => _data;

        public Task<McpSecureStorageData?> ReadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_data);
        }

        public void Update(McpSecureStorageData data)
        {
            throw new NotSupportedException();
        }

        public bool Delete()
        {
            throw new NotSupportedException();
        }
    }
}
