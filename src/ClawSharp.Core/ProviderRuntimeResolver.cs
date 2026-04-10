using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClawSharp.Core;

public enum ApiProviderKind
{
    Anthropic,
    OpenAi,
    Gemini,
    GitHub,
    Codex,
    Bedrock,
    Vertex,
    Foundry
}

public enum ModelTransportKind
{
    AnthropicMessages,
    OpenAiChatCompletions,
    CodexResponses
}

public sealed record ProviderRuntimeConfig(
    ApiProviderKind Provider,
    ModelTransportKind Transport,
    string RequestedModel,
    string ResolvedModel,
    string BaseUrl,
    string? ApiKey = null,
    string? AuthToken = null,
    string? AccountId = null,
    string? ApiVersion = null,
    IReadOnlyDictionary<string, string>? AdditionalHeaders = null);

public sealed record GeminiCredential(
    string Kind,
    string Credential,
    string? ProjectId = null);

public static class ProviderRuntimeResolver
{
    public const string DefaultAnthropicBaseUrl = "https://api.anthropic.com";
    public const string DefaultOpenAiBaseUrl = "https://api.openai.com/v1";
    public const string DefaultCodexBaseUrl = "https://chatgpt.com/backend-api/codex";
    public const string DefaultGeminiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai";
    public const string DefaultGitHubModelsBaseUrl = "https://models.github.ai/inference";
    public const string DefaultAnthropicModel = "claude-haiku-4-5-20251001";
    public const string DefaultOpenAiModel = "gpt-4o";
    public const string DefaultGeminiModel = "gemini-2.0-flash";
    public const string DefaultGitHubModel = "openai/gpt-4.1";
    public const string DefaultCodexModel = "codexplan";

    private static readonly IReadOnlyDictionary<string, string> GitHubHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Accept"] = "application/vnd.github.v3+json",
            ["X-GitHub-Api-Version"] = "2022-11-28"
        };

    private static readonly IReadOnlyDictionary<string, string> CodexAliasModels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["codexplan"] = "gpt-5.4",
            ["gpt-5.4"] = "gpt-5.4",
            ["gpt-5.3-codex"] = "gpt-5.3-codex",
            ["gpt-5.3-codex-spark"] = "gpt-5.3-codex-spark",
            ["codexspark"] = "gpt-5.3-codex-spark",
            ["gpt-5.2-codex"] = "gpt-5.2-codex",
            ["gpt-5.1-codex-max"] = "gpt-5.1-codex-max",
            ["gpt-5.1-codex-mini"] = "gpt-5.1-codex-mini",
            ["gpt-5.4-mini"] = "gpt-5.4-mini",
            ["gpt-5.2"] = "gpt-5.2"
        };

    public static ProviderRuntimeConfig Resolve(
        ClawSharpSettings settings,
        string? requestedModel = null,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        if (TryResolveModelConnection(settings, requestedModel, getEnvironmentVariable, out var modelConnectionConfig))
        {
            return modelConnectionConfig;
        }

        var provider = GetProvider(getEnvironmentVariable);

        return provider switch
        {
            ApiProviderKind.Gemini => ResolveGemini(settings, requestedModel, getEnvironmentVariable),
            ApiProviderKind.GitHub => ResolveGitHub(settings, requestedModel, getEnvironmentVariable),
            ApiProviderKind.Codex => ResolveCodex(settings, requestedModel, getEnvironmentVariable),
            ApiProviderKind.OpenAi => ResolveOpenAi(settings, requestedModel, getEnvironmentVariable),
            ApiProviderKind.Bedrock => ResolveBedrock(settings, requestedModel, getEnvironmentVariable),
            ApiProviderKind.Vertex => ResolveVertex(settings, requestedModel, getEnvironmentVariable),
            ApiProviderKind.Foundry => ResolveFoundry(settings, requestedModel, getEnvironmentVariable),
            _ => ResolveAnthropicLike(settings, requestedModel, getEnvironmentVariable, ApiProviderKind.Anthropic)
        };
    }

    public static ApiProviderKind GetProvider(Func<string, string?>? getEnvironmentVariable = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;

        if (IsTruthy(getEnvironmentVariable("CLAUDE_CODE_USE_GEMINI")))
        {
            return ApiProviderKind.Gemini;
        }

        if (IsTruthy(getEnvironmentVariable("CLAUDE_CODE_USE_GITHUB")))
        {
            return ApiProviderKind.GitHub;
        }

        if (IsTruthy(getEnvironmentVariable("CLAUDE_CODE_USE_OPENAI")))
        {
            var requestedModel =
                FirstNonEmpty(
                    getEnvironmentVariable("OPENAI_MODEL"),
                    getEnvironmentVariable("CLAUDE_CODE_OPENAI_MODEL")) ??
                DefaultOpenAiModel;
            var baseUrl =
                FirstNonEmpty(
                    SanitizeEnvUrl(getEnvironmentVariable("OPENAI_BASE_URL")),
                    SanitizeEnvUrl(getEnvironmentVariable("OPENAI_API_BASE")));
            return IsCodexTransport(requestedModel, baseUrl)
                ? ApiProviderKind.Codex
                : ApiProviderKind.OpenAi;
        }

        if (IsTruthy(getEnvironmentVariable("CLAUDE_CODE_USE_BEDROCK")))
        {
            return ApiProviderKind.Bedrock;
        }

        if (IsTruthy(getEnvironmentVariable("CLAUDE_CODE_USE_VERTEX")))
        {
            return ApiProviderKind.Vertex;
        }

        if (IsTruthy(getEnvironmentVariable("CLAUDE_CODE_USE_FOUNDRY")))
        {
            return ApiProviderKind.Foundry;
        }

        return ApiProviderKind.Anthropic;
    }

    public static bool IsCodexAlias(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        var normalized = model.Trim().Split('?', 2)[0];
        return CodexAliasModels.ContainsKey(normalized);
    }

    public static string ResolveModelAlias(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return model;
        }

        var queryIndex = model.IndexOf('?');
        var baseModel = queryIndex >= 0
            ? model[..queryIndex].Trim()
            : model.Trim();

        return CodexAliasModels.TryGetValue(baseModel, out var resolvedModel)
            ? resolvedModel
            : baseModel;
    }

    public static bool IsLocalProviderUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.Trim('[', ']').ToLowerInvariant();
        var zoneIndex = host.IndexOf("%25", StringComparison.Ordinal);
        if (zoneIndex >= 0)
        {
            host = host[..zoneIndex];
        }

        if (host is "localhost" or "127.0.0.1" or "::1" or "0.0.0.0")
        {
            return true;
        }

        if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IPAddress.TryParse(host, out var address))
        {
            if (IPAddress.IsLoopback(address))
            {
                return true;
            }

            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                var bytes = address.GetAddressBytes();
                return bytes[0] == 10 ||
                       (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                       (bytes[0] == 192 && bytes[1] == 168);
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                var bytes = address.GetAddressBytes();
                return (bytes[0] & 0xfe) == 0xfc ||
                       (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80);
            }
        }

        return false;
    }

    public static GeminiCredential? ResolveGeminiCredential(Func<string, string?>? getEnvironmentVariable = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;

        var authMode = FirstNonEmpty(getEnvironmentVariable("GEMINI_AUTH_MODE"))?.Trim().ToLowerInvariant();
        var projectId =
            FirstNonEmpty(
                getEnvironmentVariable("GOOGLE_CLOUD_PROJECT"),
                getEnvironmentVariable("GCLOUD_PROJECT"),
                getEnvironmentVariable("GOOGLE_PROJECT_ID"));

        var apiKey = authMode is "access-token" or "adc"
            ? null
            : FirstNonEmpty(
                getEnvironmentVariable("GEMINI_API_KEY"),
                getEnvironmentVariable("GOOGLE_API_KEY"));
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            return new GeminiCredential("api-key", apiKey.Trim(), projectId);
        }

        var accessToken = authMode is "api-key" or "adc"
            ? null
            : FirstNonEmpty(getEnvironmentVariable("GEMINI_ACCESS_TOKEN"));
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            return new GeminiCredential("access-token", accessToken.Trim(), projectId);
        }

        return null;
    }

    public static (string? ApiKey, string? AccountId, string? AuthPath) ResolveCodexCredentials(Func<string, string?>? getEnvironmentVariable = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        var apiKey = FirstNonEmpty(getEnvironmentVariable("CODEX_API_KEY"));
        var accountId = FirstNonEmpty(
            getEnvironmentVariable("CODEX_ACCOUNT_ID"),
            getEnvironmentVariable("CHATGPT_ACCOUNT_ID"));

        if (!string.IsNullOrWhiteSpace(apiKey) &&
            !LooksLikeOpenAiProjectApiKey(apiKey))
        {
            return (apiKey.Trim(), accountId ?? ParseChatGptAccountId(apiKey), null);
        }

        var authPath = ResolveCodexAuthPath(getEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(authPath) || !File.Exists(authPath))
        {
            return string.IsNullOrWhiteSpace(apiKey)
                ? (null, accountId, authPath)
                : (apiKey.Trim(), accountId ?? ParseChatGptAccountId(apiKey), authPath);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(authPath));
            var root = document.RootElement;
            var authJsonApiKey =
                ReadJsonString(root, "access_token") ??
                ReadJsonString(root, "accessToken") ??
                ReadNestedJsonString(root, "tokens", "access_token") ??
                ReadNestedJsonString(root, "tokens", "accessToken") ??
                ReadNestedJsonString(root, "auth", "access_token") ??
                ReadNestedJsonString(root, "auth", "accessToken") ??
                ReadNestedJsonString(root, "token", "access_token") ??
                ReadNestedJsonString(root, "token", "accessToken") ??
                ReadNestedJsonString(root, "tokens", "id_token") ??
                ReadNestedJsonString(root, "tokens", "idToken");

            accountId ??=
                ReadJsonString(root, "account_id") ??
                ReadJsonString(root, "accountId") ??
                ReadNestedJsonString(root, "auth", "account_id") ??
                ReadNestedJsonString(root, "auth", "accountId") ??
                ReadNestedJsonString(root, "tokens", "account_id") ??
                ReadNestedJsonString(root, "tokens", "accountId");

            if (!string.IsNullOrWhiteSpace(authJsonApiKey))
            {
                return (authJsonApiKey, accountId ?? ParseChatGptAccountId(authJsonApiKey), authPath);
            }

            return string.IsNullOrWhiteSpace(apiKey)
                ? (null, accountId, authPath)
                : (apiKey.Trim(), accountId ?? ParseChatGptAccountId(apiKey), authPath);
        }
        catch
        {
            return string.IsNullOrWhiteSpace(apiKey)
                ? (null, accountId, authPath)
                : (apiKey.Trim(), accountId ?? ParseChatGptAccountId(apiKey), authPath);
        }
    }

    public static string GetDefaultModelForCurrentProvider(Func<string, string?>? getEnvironmentVariable = null)
    {
        return GetProvider(getEnvironmentVariable) switch
        {
            ApiProviderKind.Gemini => DefaultGeminiModel,
            ApiProviderKind.GitHub => DefaultGitHubModel,
            ApiProviderKind.Codex => ResolveModelAlias(DefaultCodexModel),
            ApiProviderKind.OpenAi => DefaultOpenAiModel,
            _ => DefaultAnthropicModel
        };
    }

    public static string NormalizeGitHubModel(string requestedModel)
    {
        if (string.IsNullOrWhiteSpace(requestedModel))
        {
            return DefaultGitHubModel;
        }

        var noQuery = requestedModel.Split('?', 2)[0];
        var segment = noQuery.Contains(':', StringComparison.Ordinal)
            ? noQuery.Split(':', 2)[1].Trim()
            : noQuery.Trim();

        return string.IsNullOrWhiteSpace(segment) ||
               string.Equals(segment, "copilot", StringComparison.OrdinalIgnoreCase)
            ? DefaultGitHubModel
            : segment;
    }

    private static bool TryResolveModelConnection(
        ClawSharpSettings settings,
        string? requestedModel,
        Func<string, string?> getEnvironmentVariable,
        out ProviderRuntimeConfig config)
    {
        config = null!;

        if (settings.AgentModels.Count == 0)
        {
            return false;
        }

        foreach (var candidate in EnumerateModelConnectionCandidates(settings, requestedModel))
        {
            if (!settings.AgentModels.TryGetValue(candidate, out var connection))
            {
                continue;
            }

            config = CreateModelConnectionConfig(candidate, connection, getEnvironmentVariable);
            return true;
        }

        return false;
    }

    private static ProviderRuntimeConfig ResolveAnthropicLike(
        ClawSharpSettings settings,
        string? requestedModel,
        Func<string, string?> getEnvironmentVariable,
        ApiProviderKind provider)
    {
        var resolvedRequestedModel = ResolveRequestedModel(
            requestedModel,
            settings.Runtime.Model,
            FirstNonEmpty(getEnvironmentVariable("ANTHROPIC_MODEL"), getEnvironmentVariable("CLAUDE_MODEL")),
            DefaultAnthropicModel);
        var baseUrl = FirstNonEmpty(
            SanitizeEnvUrl(getEnvironmentVariable("ANTHROPIC_BASE_URL")),
            DefaultAnthropicBaseUrl)!;

        return new ProviderRuntimeConfig(
            provider,
            ModelTransportKind.AnthropicMessages,
            resolvedRequestedModel,
            resolvedRequestedModel,
            baseUrl);
    }

    private static ProviderRuntimeConfig ResolveBedrock(
        ClawSharpSettings settings,
        string? requestedModel,
        Func<string, string?> getEnvironmentVariable)
    {
        var resolvedRequestedModel = ResolveRequestedModel(
            requestedModel,
            settings.Runtime.Model,
            FirstNonEmpty(getEnvironmentVariable("BEDROCK_MODEL"), getEnvironmentVariable("ANTHROPIC_MODEL"), getEnvironmentVariable("CLAUDE_MODEL")),
            DefaultAnthropicModel);
        var baseUrl = FirstNonEmpty(
            SanitizeEnvUrl(getEnvironmentVariable("BEDROCK_BASE_URL")),
            SanitizeEnvUrl(getEnvironmentVariable("ANTHROPIC_BASE_URL")),
            DefaultAnthropicBaseUrl)!;

        return new ProviderRuntimeConfig(
            ApiProviderKind.Bedrock,
            ModelTransportKind.AnthropicMessages,
            resolvedRequestedModel,
            resolvedRequestedModel,
            baseUrl,
            ApiKey: FirstNonEmpty(getEnvironmentVariable("BEDROCK_API_KEY")),
            AuthToken: FirstNonEmpty(getEnvironmentVariable("AWS_BEARER_TOKEN_BEDROCK"), getEnvironmentVariable("BEDROCK_AUTH_TOKEN")));
    }

    private static ProviderRuntimeConfig ResolveVertex(
        ClawSharpSettings settings,
        string? requestedModel,
        Func<string, string?> getEnvironmentVariable)
    {
        var resolvedRequestedModel = ResolveRequestedModel(
            requestedModel,
            settings.Runtime.Model,
            FirstNonEmpty(getEnvironmentVariable("VERTEX_MODEL"), getEnvironmentVariable("ANTHROPIC_MODEL"), getEnvironmentVariable("CLAUDE_MODEL")),
            DefaultAnthropicModel);
        var baseUrl = FirstNonEmpty(
            SanitizeEnvUrl(getEnvironmentVariable("VERTEX_BASE_URL")),
            SanitizeEnvUrl(getEnvironmentVariable("ANTHROPIC_BASE_URL")),
            DefaultAnthropicBaseUrl)!;
        Dictionary<string, string>? headers = null;
        var projectId = FirstNonEmpty(
            getEnvironmentVariable("ANTHROPIC_VERTEX_PROJECT_ID"),
            getEnvironmentVariable("GOOGLE_CLOUD_PROJECT"),
            getEnvironmentVariable("GCLOUD_PROJECT"));
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["x-goog-user-project"] = projectId
            };
        }

        return new ProviderRuntimeConfig(
            ApiProviderKind.Vertex,
            ModelTransportKind.AnthropicMessages,
            resolvedRequestedModel,
            resolvedRequestedModel,
            baseUrl,
            AuthToken: FirstNonEmpty(getEnvironmentVariable("VERTEX_AUTH_TOKEN"), getEnvironmentVariable("GOOGLE_ACCESS_TOKEN")),
            AdditionalHeaders: headers);
    }

    private static ProviderRuntimeConfig ResolveFoundry(
        ClawSharpSettings settings,
        string? requestedModel,
        Func<string, string?> getEnvironmentVariable)
    {
        var resolvedRequestedModel = ResolveRequestedModel(
            requestedModel,
            settings.Runtime.Model,
            FirstNonEmpty(getEnvironmentVariable("ANTHROPIC_FOUNDRY_MODEL"), getEnvironmentVariable("ANTHROPIC_MODEL"), getEnvironmentVariable("CLAUDE_MODEL")),
            DefaultAnthropicModel);
        var resource = FirstNonEmpty(getEnvironmentVariable("ANTHROPIC_FOUNDRY_RESOURCE"));
        var inferredBaseUrl = string.IsNullOrWhiteSpace(resource)
            ? null
            : $"https://{resource}.services.ai.azure.com/anthropic";
        var baseUrl = FirstNonEmpty(
            SanitizeEnvUrl(getEnvironmentVariable("ANTHROPIC_FOUNDRY_BASE_URL")),
            inferredBaseUrl,
            SanitizeEnvUrl(getEnvironmentVariable("ANTHROPIC_BASE_URL")),
            DefaultAnthropicBaseUrl)!;

        return new ProviderRuntimeConfig(
            ApiProviderKind.Foundry,
            ModelTransportKind.AnthropicMessages,
            resolvedRequestedModel,
            resolvedRequestedModel,
            baseUrl,
            ApiKey: FirstNonEmpty(getEnvironmentVariable("ANTHROPIC_FOUNDRY_API_KEY")),
            AuthToken: FirstNonEmpty(getEnvironmentVariable("ANTHROPIC_FOUNDRY_AUTH_TOKEN")));
    }

    private static ProviderRuntimeConfig CreateModelConnectionConfig(
        string requestedModel,
        AgentModelConnection connection,
        Func<string, string?> getEnvironmentVariable)
    {
        var provider = ParseConfiguredProvider(connection.Provider, connection.BaseUrl, requestedModel);
        var baseUrl = ResolveConnectionBaseUrl(connection.BaseUrl, provider);
        var additionalHeaders = MergeHeaders(provider, connection.Headers);
        var normalizedRequestedModel = requestedModel.Trim();

        return provider switch
        {
            ApiProviderKind.Anthropic or ApiProviderKind.Bedrock or ApiProviderKind.Vertex or ApiProviderKind.Foundry =>
                new ProviderRuntimeConfig(
                    provider,
                    ModelTransportKind.AnthropicMessages,
                    normalizedRequestedModel,
                    normalizedRequestedModel,
                    baseUrl,
                    ApiKey: connection.ApiKey,
                    AuthToken: connection.AuthToken,
                    AccountId: connection.AccountId,
                    ApiVersion: connection.ApiVersion,
                    AdditionalHeaders: additionalHeaders),
            ApiProviderKind.Gemini =>
                new ProviderRuntimeConfig(
                    provider,
                    ModelTransportKind.OpenAiChatCompletions,
                    normalizedRequestedModel,
                    ResolveModelAlias(normalizedRequestedModel),
                    baseUrl,
                    ApiKey: connection.ApiKey,
                    AuthToken: connection.AuthToken,
                    AccountId: connection.AccountId,
                    ApiVersion: connection.ApiVersion,
                    AdditionalHeaders: additionalHeaders),
            ApiProviderKind.GitHub =>
                new ProviderRuntimeConfig(
                    provider,
                    ModelTransportKind.OpenAiChatCompletions,
                    normalizedRequestedModel,
                    NormalizeGitHubModel(normalizedRequestedModel),
                    baseUrl,
                    ApiKey: connection.ApiKey,
                    AuthToken: connection.AuthToken,
                    AccountId: connection.AccountId,
                    ApiVersion: connection.ApiVersion,
                    AdditionalHeaders: additionalHeaders),
            ApiProviderKind.Codex =>
                CreateCodexConnectionConfig(connection, normalizedRequestedModel, baseUrl, additionalHeaders, getEnvironmentVariable),
            _ =>
                new ProviderRuntimeConfig(
                    ApiProviderKind.OpenAi,
                    ModelTransportKind.OpenAiChatCompletions,
                    normalizedRequestedModel,
                    ResolveModelAlias(normalizedRequestedModel),
                    baseUrl,
                    ApiKey: connection.ApiKey,
                    AuthToken: connection.AuthToken,
                    AccountId: connection.AccountId,
                    ApiVersion: connection.ApiVersion,
                    AdditionalHeaders: additionalHeaders)
        };
    }

    private static ProviderRuntimeConfig ResolveOpenAi(
        ClawSharpSettings settings,
        string? requestedModel,
        Func<string, string?> getEnvironmentVariable)
    {
        var resolvedRequestedModel = ResolveRequestedModel(
            requestedModel,
            settings.Runtime.Model,
            getEnvironmentVariable("OPENAI_MODEL"),
            DefaultOpenAiModel);
        var baseUrl = FirstNonEmpty(
            SanitizeEnvUrl(getEnvironmentVariable("OPENAI_BASE_URL")),
            SanitizeEnvUrl(getEnvironmentVariable("OPENAI_API_BASE")),
            DefaultOpenAiBaseUrl)!;

        return new ProviderRuntimeConfig(
            ApiProviderKind.OpenAi,
            ModelTransportKind.OpenAiChatCompletions,
            resolvedRequestedModel,
            ResolveModelAlias(resolvedRequestedModel),
            baseUrl,
            ApiKey: FirstNonEmpty(getEnvironmentVariable("OPENAI_API_KEY")),
            ApiVersion: FirstNonEmpty(getEnvironmentVariable("AZURE_OPENAI_API_VERSION"), getEnvironmentVariable("OPENAI_API_VERSION")));
    }

    private static ProviderRuntimeConfig ResolveGemini(
        ClawSharpSettings settings,
        string? requestedModel,
        Func<string, string?> getEnvironmentVariable)
    {
        var resolvedRequestedModel = ResolveRequestedModel(
            requestedModel,
            settings.Runtime.Model,
            getEnvironmentVariable("GEMINI_MODEL"),
            DefaultGeminiModel);
        var baseUrl = FirstNonEmpty(
            SanitizeEnvUrl(getEnvironmentVariable("GEMINI_BASE_URL")),
            SanitizeEnvUrl(getEnvironmentVariable("OPENAI_BASE_URL")),
            DefaultGeminiBaseUrl)!;
        var credential = ResolveGeminiCredential(getEnvironmentVariable);
        Dictionary<string, string>? headers = null;
        if (!string.IsNullOrWhiteSpace(credential?.ProjectId))
        {
            headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["x-goog-user-project"] = credential.ProjectId
            };
        }

        return new ProviderRuntimeConfig(
            ApiProviderKind.Gemini,
            ModelTransportKind.OpenAiChatCompletions,
            resolvedRequestedModel,
            ResolveModelAlias(resolvedRequestedModel),
            baseUrl,
            ApiKey: credential is { Kind: "api-key" } ? credential.Credential : null,
            AuthToken: credential is { Kind: not "api-key" } ? credential?.Credential : null,
            AdditionalHeaders: headers);
    }

    private static ProviderRuntimeConfig ResolveGitHub(
        ClawSharpSettings settings,
        string? requestedModel,
        Func<string, string?> getEnvironmentVariable)
    {
        var rawRequestedModel = ResolveRequestedModel(
            requestedModel,
            settings.Runtime.Model,
            getEnvironmentVariable("OPENAI_MODEL"),
            "github:copilot");
        var normalizedRequestedModel = string.Equals(rawRequestedModel, DefaultAnthropicModel, StringComparison.Ordinal)
            ? "github:copilot"
            : rawRequestedModel;

        return new ProviderRuntimeConfig(
            ApiProviderKind.GitHub,
            ModelTransportKind.OpenAiChatCompletions,
            normalizedRequestedModel,
            NormalizeGitHubModel(normalizedRequestedModel),
            DefaultGitHubModelsBaseUrl,
            AuthToken: FirstNonEmpty(getEnvironmentVariable("GITHUB_TOKEN"), getEnvironmentVariable("GH_TOKEN")),
            AdditionalHeaders: GitHubHeaders);
    }

    private static ProviderRuntimeConfig ResolveCodex(
        ClawSharpSettings settings,
        string? requestedModel,
        Func<string, string?> getEnvironmentVariable)
    {
        var rawRequestedModel = ResolveRequestedModel(
            requestedModel,
            settings.Runtime.Model,
            getEnvironmentVariable("OPENAI_MODEL"),
            DefaultCodexModel);
        var baseUrl = FirstNonEmpty(
            SanitizeEnvUrl(getEnvironmentVariable("OPENAI_BASE_URL")),
            SanitizeEnvUrl(getEnvironmentVariable("OPENAI_API_BASE")),
            DefaultCodexBaseUrl)!;
        var credentials = ResolveCodexCredentials(getEnvironmentVariable);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["originator"] = "clawsharp"
        };

        return new ProviderRuntimeConfig(
            ApiProviderKind.Codex,
            ModelTransportKind.CodexResponses,
            rawRequestedModel,
            ResolveModelAlias(rawRequestedModel),
            baseUrl,
            ApiKey: credentials.ApiKey,
            AccountId: credentials.AccountId,
            AdditionalHeaders: headers);
    }

    private static ProviderRuntimeConfig CreateCodexConnectionConfig(
        AgentModelConnection connection,
        string normalizedRequestedModel,
        string baseUrl,
        IReadOnlyDictionary<string, string>? additionalHeaders,
        Func<string, string?> getEnvironmentVariable)
    {
        var externalCredentials = connection.UseExternalCredential
            ? ResolveCodexCredentials(getEnvironmentVariable)
            : (ApiKey: (string?)null, AccountId: (string?)null, AuthPath: (string?)null);

        return new ProviderRuntimeConfig(
            ApiProviderKind.Codex,
            ModelTransportKind.CodexResponses,
            normalizedRequestedModel,
            ResolveModelAlias(normalizedRequestedModel),
            baseUrl,
            ApiKey: connection.UseExternalCredential
                ? externalCredentials.ApiKey
                : connection.ApiKey ?? connection.AuthToken,
            AccountId: connection.UseExternalCredential
                ? externalCredentials.AccountId ?? connection.AccountId
                : connection.AccountId,
            ApiVersion: connection.ApiVersion,
            AdditionalHeaders: additionalHeaders);
    }

    private static string ResolveRequestedModel(
        string? requestedModel,
        string? settingsModel,
        string? environmentModel,
        string fallbackModel)
    {
        if (!string.IsNullOrWhiteSpace(requestedModel))
        {
            return requestedModel.Trim();
        }

        if (!string.IsNullOrWhiteSpace(environmentModel))
        {
            return environmentModel.Trim();
        }

        if (!string.IsNullOrWhiteSpace(settingsModel) &&
            !string.Equals(settingsModel.Trim(), MainLoopModelResolver.PlaceholderModel, StringComparison.OrdinalIgnoreCase))
        {
            return settingsModel.Trim();
        }

        return fallbackModel;
    }

    private static IEnumerable<string> EnumerateModelConnectionCandidates(
        ClawSharpSettings settings,
        string? requestedModel)
    {
        if (!string.IsNullOrWhiteSpace(requestedModel))
        {
            yield return requestedModel.Trim();

            var resolvedRequestedModel = ResolveModelAlias(requestedModel);
            if (!string.Equals(resolvedRequestedModel, requestedModel.Trim(), StringComparison.Ordinal))
            {
                yield return resolvedRequestedModel;
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.Runtime.Model) &&
            !string.Equals(settings.Runtime.Model.Trim(), MainLoopModelResolver.PlaceholderModel, StringComparison.OrdinalIgnoreCase))
        {
            yield return settings.Runtime.Model.Trim();
        }
    }

    private static ApiProviderKind ParseConfiguredProvider(string? provider, string? baseUrl, string requestedModel)
    {
        if (!string.IsNullOrWhiteSpace(provider))
        {
            return provider.Trim().ToLowerInvariant() switch
            {
                "anthropic" => ApiProviderKind.Anthropic,
                "gemini" => ApiProviderKind.Gemini,
                "github" => ApiProviderKind.GitHub,
                "codex" => ApiProviderKind.Codex,
                "bedrock" => ApiProviderKind.Bedrock,
                "vertex" => ApiProviderKind.Vertex,
                "foundry" => ApiProviderKind.Foundry,
                _ => ApiProviderKind.OpenAi
            };
        }

        var normalizedBaseUrl = SanitizeEnvUrl(baseUrl);
        if (IsCodexTransport(requestedModel, normalizedBaseUrl))
        {
            return ApiProviderKind.Codex;
        }

        if (!string.IsNullOrWhiteSpace(normalizedBaseUrl) &&
            Uri.TryCreate(normalizedBaseUrl, UriKind.Absolute, out var uri))
        {
            var host = uri.Host.ToLowerInvariant();
            if (host.Contains("generativelanguage.googleapis.com", StringComparison.Ordinal))
            {
                return ApiProviderKind.Gemini;
            }

            if (host.Contains("models.github.ai", StringComparison.Ordinal))
            {
                return ApiProviderKind.GitHub;
            }

            if (host.Contains("anthropic.com", StringComparison.Ordinal))
            {
                return ApiProviderKind.Anthropic;
            }
        }

        return ApiProviderKind.OpenAi;
    }

    private static string ResolveConnectionBaseUrl(string? baseUrl, ApiProviderKind provider)
    {
        var normalizedBaseUrl = SanitizeEnvUrl(baseUrl);
        if (!string.IsNullOrWhiteSpace(normalizedBaseUrl))
        {
            return normalizedBaseUrl;
        }

        return provider switch
        {
            ApiProviderKind.Anthropic or ApiProviderKind.Bedrock or ApiProviderKind.Vertex or ApiProviderKind.Foundry => DefaultAnthropicBaseUrl,
            ApiProviderKind.Gemini => DefaultGeminiBaseUrl,
            ApiProviderKind.GitHub => DefaultGitHubModelsBaseUrl,
            ApiProviderKind.Codex => DefaultCodexBaseUrl,
            _ => DefaultOpenAiBaseUrl
        };
    }

    private static IReadOnlyDictionary<string, string>? MergeHeaders(
        ApiProviderKind provider,
        IReadOnlyDictionary<string, string>? headers)
    {
        Dictionary<string, string>? merged = null;

        if (provider == ApiProviderKind.GitHub)
        {
            merged = new Dictionary<string, string>(GitHubHeaders, StringComparer.OrdinalIgnoreCase);
        }
        else if (provider == ApiProviderKind.Codex)
        {
            merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["originator"] = "clawsharp"
            };
        }

        if (headers is null || headers.Count == 0)
        {
            return merged;
        }

        merged ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in headers)
        {
            merged[pair.Key] = pair.Value;
        }

        return merged;
    }

    private static bool IsCodexTransport(string requestedModel, string? baseUrl)
    {
        return IsCodexBaseUrl(baseUrl) || (string.IsNullOrWhiteSpace(baseUrl) && IsCodexAlias(requestedModel));
    }

    private static bool IsCodexBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var normalizedPath = uri.AbsolutePath.TrimEnd('/');
        return string.Equals(uri.Host, "chatgpt.com", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(normalizedPath, "/backend-api/codex", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveCodexAuthPath(Func<string, string?> getEnvironmentVariable)
    {
        var explicitPath = FirstNonEmpty(getEnvironmentVariable("CODEX_AUTH_JSON_PATH"));
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath.Trim();
        }

        var codexHome = FirstNonEmpty(getEnvironmentVariable("CODEX_HOME"));
        if (!string.IsNullOrWhiteSpace(codexHome))
        {
            return Path.Combine(codexHome.Trim(), "auth.json");
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".codex", "auth.json");
    }

    private static string? ParseChatGptAccountId(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var parts = token.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            var payloadBytes = DecodeBase64Url(parts[1]);
            using var document = JsonDocument.Parse(payloadBytes);
            return ReadJsonString(document.RootElement, "https://api.openai.com/auth.chatgpt_account_id") ??
                   ReadJsonString(document.RootElement, "chatgpt_account_id");
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksLikeOpenAiProjectApiKey(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        return token.TrimStart().StartsWith("sk-", StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        var padding = 4 - normalized.Length % 4;
        if (padding is > 0 and < 4)
        {
            normalized = normalized.PadRight(normalized.Length + padding, '=');
        }

        return Convert.FromBase64String(normalized);
    }

    private static string? ReadJsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = property.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? ReadNestedJsonString(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out current))
            {
                return null;
            }
        }

        return current.ValueKind == JsonValueKind.String
            ? current.GetString()?.Trim()
            : null;
    }

    private static string? SanitizeEnvUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return string.Equals(trimmed, "undefined", StringComparison.OrdinalIgnoreCase)
            ? null
            : trimmed.TrimEnd('/');
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
    }
}
