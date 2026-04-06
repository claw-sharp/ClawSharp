using ClawSharp.Core;

namespace ClawSharp.UnitTests;

public sealed class ProviderRuntimeResolverTests
{
    [Fact]
    public void Resolve_Defaults_To_Anthropic_When_No_Third_Party_Flags_Are_Set()
    {
        var config = ProviderRuntimeResolver.Resolve(new ClawSharpSettings(), getEnvironmentVariable: _ => null);

        Assert.Equal(ApiProviderKind.Anthropic, config.Provider);
        Assert.Equal(ModelTransportKind.AnthropicMessages, config.Transport);
        Assert.Equal(ProviderRuntimeResolver.DefaultAnthropicBaseUrl, config.BaseUrl);
        Assert.Equal(ProviderRuntimeResolver.DefaultAnthropicModel, config.ResolvedModel);
    }

    [Fact]
    public void Resolve_Uses_OpenAi_Compatible_Transport_When_OpenAi_Flag_Is_Set()
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["CLAUDE_CODE_USE_OPENAI"] = "1",
            ["OPENAI_BASE_URL"] = "https://api.openai.com/v1",
            ["OPENAI_MODEL"] = "gpt-4o",
            ["OPENAI_API_KEY"] = "sk-openai"
        };

        var config = ProviderRuntimeResolver.Resolve(
            new ClawSharpSettings(),
            getEnvironmentVariable: name => env.TryGetValue(name, out var value) ? value : null);

        Assert.Equal(ApiProviderKind.OpenAi, config.Provider);
        Assert.Equal(ModelTransportKind.OpenAiChatCompletions, config.Transport);
        Assert.Equal("gpt-4o", config.ResolvedModel);
        Assert.Equal("sk-openai", config.ApiKey);
    }

    [Fact]
    public void Resolve_Uses_Codex_Transport_For_Codex_Alias_Model()
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["CLAUDE_CODE_USE_OPENAI"] = "1",
            ["OPENAI_MODEL"] = "codexplan",
            ["CODEX_API_KEY"] = "codex-token",
            ["CHATGPT_ACCOUNT_ID"] = "account-123"
        };

        var config = ProviderRuntimeResolver.Resolve(
            new ClawSharpSettings(),
            getEnvironmentVariable: name => env.TryGetValue(name, out var value) ? value : null);

        Assert.Equal(ApiProviderKind.Codex, config.Provider);
        Assert.Equal(ModelTransportKind.CodexResponses, config.Transport);
        Assert.Equal("gpt-5.4", config.ResolvedModel);
        Assert.Equal("codex-token", config.ApiKey);
        Assert.Equal("account-123", config.AccountId);
    }

    [Fact]
    public void ResolveCodexCredentials_Prefers_AuthJson_When_CodexApiKey_Looks_Like_OpenAi_Key()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-codex-auth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var authPath = Path.Combine(tempDir, "auth.json");
        File.WriteAllText(
            authPath,
            """
            {
              "tokens": {
                "access_token": "eyJhbGciOiJIUzI1NiJ9.payload.signature",
                "account_id": "account-from-auth"
              }
            }
            """);
        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["CODEX_API_KEY"] = "sk-proj-test",
            ["CODEX_AUTH_JSON_PATH"] = authPath
        };

        var credentials = ProviderRuntimeResolver.ResolveCodexCredentials(
            name => env.TryGetValue(name, out var value) ? value : null);

        Assert.Equal("eyJhbGciOiJIUzI1NiJ9.payload.signature", credentials.ApiKey);
        Assert.Equal("account-from-auth", credentials.AccountId);
        Assert.Equal(authPath, credentials.AuthPath);
    }

    [Fact]
    public void Resolve_Uses_Gemini_Credential_And_Project_Header()
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["CLAUDE_CODE_USE_GEMINI"] = "1",
            ["GEMINI_API_KEY"] = "AIza-test",
            ["GEMINI_MODEL"] = "gemini-2.0-flash",
            ["GOOGLE_CLOUD_PROJECT"] = "proj-1"
        };

        var config = ProviderRuntimeResolver.Resolve(
            new ClawSharpSettings(),
            getEnvironmentVariable: name => env.TryGetValue(name, out var value) ? value : null);

        Assert.Equal(ApiProviderKind.Gemini, config.Provider);
        Assert.Equal(ModelTransportKind.OpenAiChatCompletions, config.Transport);
        Assert.Equal("AIza-test", config.ApiKey);
        Assert.Equal("proj-1", config.AdditionalHeaders!["x-goog-user-project"]);
    }

    [Fact]
    public void Resolve_Uses_Agent_Model_Connection_Before_Global_Provider_Flags()
    {
        var settings = new ClawSharpSettings
        {
            Runtime = new RuntimeSettings
            {
                Model = "deepseek-chat"
            },
            AgentModels = new Dictionary<string, AgentModelConnection>(StringComparer.Ordinal)
            {
                ["deepseek-chat"] = new()
                {
                    Provider = "openai",
                    BaseUrl = "https://api.deepseek.com/v1",
                    ApiKey = "deepseek-key"
                }
            }
        };
        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["CLAUDE_CODE_USE_OPENAI"] = "1",
            ["OPENAI_MODEL"] = "gpt-4o",
            ["OPENAI_API_KEY"] = "env-openai-key"
        };

        var config = ProviderRuntimeResolver.Resolve(
            settings,
            requestedModel: "deepseek-chat",
            getEnvironmentVariable: name => env.TryGetValue(name, out var value) ? value : null);

        Assert.Equal(ApiProviderKind.OpenAi, config.Provider);
        Assert.Equal("https://api.deepseek.com/v1", config.BaseUrl);
        Assert.Equal("deepseek-chat", config.ResolvedModel);
        Assert.Equal("deepseek-key", config.ApiKey);
    }

    [Fact]
    public void Resolve_Uses_GitHub_Model_Connection_Headers()
    {
        var settings = new ClawSharpSettings
        {
            Runtime = new RuntimeSettings
            {
                Model = "github:copilot"
            },
            AgentModels = new Dictionary<string, AgentModelConnection>(StringComparer.Ordinal)
            {
                ["github:copilot"] = new()
                {
                    Provider = "github",
                    BaseUrl = ProviderRuntimeResolver.DefaultGitHubModelsBaseUrl,
                    AuthToken = "gh-token"
                }
            }
        };

        var config = ProviderRuntimeResolver.Resolve(settings, requestedModel: "github:copilot", getEnvironmentVariable: _ => null);

        Assert.Equal(ApiProviderKind.GitHub, config.Provider);
        Assert.Equal("openai/gpt-4.1", config.ResolvedModel);
        Assert.Equal("gh-token", config.AuthToken);
        Assert.Equal("application/vnd.github.v3+json", config.AdditionalHeaders!["Accept"]);
    }

    [Fact]
    public void Resolve_Uses_Foundry_Environment_BaseUrl_And_Api_Key()
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["CLAUDE_CODE_USE_FOUNDRY"] = "1",
            ["ANTHROPIC_FOUNDRY_RESOURCE"] = "my-resource",
            ["ANTHROPIC_FOUNDRY_API_KEY"] = "foundry-key"
        };

        var config = ProviderRuntimeResolver.Resolve(
            new ClawSharpSettings(),
            getEnvironmentVariable: name => env.TryGetValue(name, out var value) ? value : null);

        Assert.Equal(ApiProviderKind.Foundry, config.Provider);
        Assert.Equal(ModelTransportKind.AnthropicMessages, config.Transport);
        Assert.Equal("https://my-resource.services.ai.azure.com/anthropic", config.BaseUrl);
        Assert.Equal("foundry-key", config.ApiKey);
    }

    [Theory]
    [InlineData("http://localhost:11434/v1")]
    [InlineData("http://127.0.0.2:11434/v1")]
    [InlineData("http://192.168.1.8:11434/v1")]
    [InlineData("http://[fd00::1]:11434/v1")]
    public void IsLocalProviderUrl_Recognizes_Local_Endpoints(string baseUrl)
    {
        Assert.True(ProviderRuntimeResolver.IsLocalProviderUrl(baseUrl));
    }
}
