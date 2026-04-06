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

    [Fact]
    public void GetConfig_Resolves_OpenAi_Compatible_Provider_Config()
    {
        var previousUseOpenAi = Environment.GetEnvironmentVariable("CLAUDE_CODE_USE_OPENAI");
        var previousBaseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL");
        var previousModel = Environment.GetEnvironmentVariable("OPENAI_MODEL");
        var previousApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("CLAUDE_CODE_USE_OPENAI", "1");
            Environment.SetEnvironmentVariable("OPENAI_BASE_URL", "https://api.openai.test/v1");
            Environment.SetEnvironmentVariable("OPENAI_MODEL", "gpt-4o");
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", "sk-openai");
            var provider = new EnvironmentQueryModelHttpClientConfigProvider();

            var config = provider.GetConfig(
                CreateRequest(),
                QueryLoopStateFactory.CreateInitial([]),
                CreateSession(),
                new ClawSharpSettings());

            Assert.Equal("https://api.openai.test/v1", config.BaseUrl);
            Assert.Equal(ModelTransportKind.OpenAiChatCompletions, config.TransportKind);
            Assert.Equal(ApiProviderKind.OpenAi, config.ProviderKind);
            Assert.Equal("sk-openai", config.ApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CODE_USE_OPENAI", previousUseOpenAi);
            Environment.SetEnvironmentVariable("OPENAI_BASE_URL", previousBaseUrl);
            Environment.SetEnvironmentVariable("OPENAI_MODEL", previousModel);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", previousApiKey);
        }
    }

    [Fact]
    public void GetConfig_Resolves_Codex_Transport_For_Codex_Alias()
    {
        var previousUseOpenAi = Environment.GetEnvironmentVariable("CLAUDE_CODE_USE_OPENAI");
        var previousModel = Environment.GetEnvironmentVariable("OPENAI_MODEL");
        var previousCodexKey = Environment.GetEnvironmentVariable("CODEX_API_KEY");
        var previousAccount = Environment.GetEnvironmentVariable("CHATGPT_ACCOUNT_ID");
        try
        {
            Environment.SetEnvironmentVariable("CLAUDE_CODE_USE_OPENAI", "1");
            Environment.SetEnvironmentVariable("OPENAI_MODEL", "codexplan");
            Environment.SetEnvironmentVariable("CODEX_API_KEY", "codex-token");
            Environment.SetEnvironmentVariable("CHATGPT_ACCOUNT_ID", "account-1");
            var provider = new EnvironmentQueryModelHttpClientConfigProvider();

            var config = provider.GetConfig(
                CreateRequest(),
                QueryLoopStateFactory.CreateInitial([]),
                CreateSession(),
                new ClawSharpSettings());

            Assert.Equal(ModelTransportKind.CodexResponses, config.TransportKind);
            Assert.Equal(ApiProviderKind.Codex, config.ProviderKind);
            Assert.Equal("codex-token", config.ApiKey);
            Assert.Equal("account-1", config.AccountId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CODE_USE_OPENAI", previousUseOpenAi);
            Environment.SetEnvironmentVariable("OPENAI_MODEL", previousModel);
            Environment.SetEnvironmentVariable("CODEX_API_KEY", previousCodexKey);
            Environment.SetEnvironmentVariable("CHATGPT_ACCOUNT_ID", previousAccount);
        }
    }

    [Fact]
    public void GetConfig_Uses_Custom_Anthropic_Model_Connection_Credentials()
    {
        var previousAnthropicApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        var previousAnthropicAuthToken = Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
            Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", null);
            var provider = new EnvironmentQueryModelHttpClientConfigProvider();
            var settings = new ClawSharpSettings
            {
                Runtime = new RuntimeSettings
                {
                    Model = "claude-custom"
                },
                AgentModels = new Dictionary<string, AgentModelConnection>(StringComparer.Ordinal)
                {
                    ["claude-custom"] = new()
                    {
                        Provider = "anthropic",
                        BaseUrl = "https://anthropic-proxy.test",
                        ApiKey = "custom-anthropic-key"
                    }
                }
            };

            var config = provider.GetConfig(
                CreateRequest(),
                QueryLoopStateFactory.CreateInitial([]),
                CreateSession(),
                settings);

            Assert.Equal("https://anthropic-proxy.test", config.BaseUrl);
            Assert.Equal(ApiProviderKind.Anthropic, config.ProviderKind);
            Assert.Equal("custom-anthropic-key", config.ApiKey);
            Assert.Null(config.AuthToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", previousAnthropicApiKey);
            Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", previousAnthropicAuthToken);
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
