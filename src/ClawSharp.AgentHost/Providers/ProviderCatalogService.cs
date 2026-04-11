using ClawSharp.AgentHost.Contracts;
using ClawSharp.AgentHost.Ipc;
using ClawSharp.AgentHost.Projects;
using ClawSharp.AgentHost.Services;
using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.AgentHost.Providers;

public sealed class ProviderCatalogService
{
    private static readonly IReadOnlyList<ProviderOptionDto> ProviderOptions =
    [
        new(
            "anthropic",
            "Anthropic",
            ProviderRuntimeResolver.DefaultAnthropicModel,
            [ProviderRuntimeResolver.DefaultAnthropicModel, "claude-sonnet-4-5", "claude-opus-4-1"],
            ProviderRuntimeResolver.DefaultAnthropicBaseUrl,
            true,
            "Claude default provider selection."),
        new(
            "openai",
            "OpenAI",
            ProviderRuntimeResolver.DefaultOpenAiModel,
            [ProviderRuntimeResolver.DefaultOpenAiModel, "gpt-4.1", "o3"],
            ProviderRuntimeResolver.DefaultOpenAiBaseUrl,
            true,
            "OpenAI chat completions transport."),
        new(
            "codex",
            "Codex",
            ProviderRuntimeResolver.DefaultCodexModel,
            [ProviderRuntimeResolver.DefaultCodexModel, "gpt-5.4", "gpt-5.4-mini"],
            ProviderRuntimeResolver.DefaultCodexBaseUrl,
            true,
            "OpenAI Codex responses transport."),
        new(
            "gemini",
            "Gemini",
            ProviderRuntimeResolver.DefaultGeminiModel,
            [ProviderRuntimeResolver.DefaultGeminiModel, "gemini-2.5-pro"],
            ProviderRuntimeResolver.DefaultGeminiBaseUrl,
            true,
            "Gemini OpenAI-compatible transport."),
        new(
            "github",
            "GitHub Models",
            ProviderRuntimeResolver.DefaultGitHubModel,
            [ProviderRuntimeResolver.DefaultGitHubModel, "openai/gpt-4.1"],
            ProviderRuntimeResolver.DefaultGitHubModelsBaseUrl,
            true,
            "GitHub-hosted models."),
        new(
            "ollama",
            "Ollama",
            "llama3.2",
            ["llama3.2", "qwen2.5-coder"],
            "http://localhost:11434/v1",
            false,
            "Local OpenAI-compatible provider.")
    ];

    private readonly WorkspaceApplicationRegistry _applicationRegistry;
    private readonly RecentProjectStore _recentProjectStore;
    private readonly IMcpSecureStorage _secureStorage;
    private readonly IProviderLiveValidationService _liveValidationService;

    public ProviderCatalogService(
        WorkspaceApplicationRegistry applicationRegistry,
        RecentProjectStore recentProjectStore,
        IMcpSecureStorage? secureStorage = null,
        IProviderLiveValidationService? liveValidationService = null)
    {
        _applicationRegistry = applicationRegistry;
        _recentProjectStore = recentProjectStore;
        _secureStorage = secureStorage ?? McpSecureStorageFactory.CreateDefault();
        _liveValidationService = liveValidationService ?? new ProviderLiveValidationService(_secureStorage);
    }

    public Task<ListProvidersResponse> ListProvidersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ListProvidersResponse(ProviderOptions));
    }

    public async Task<GetSettingsResponse> GetSettingsAsync(
        GetSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId))
        {
            var settings = await LoadUserSettingsAsync(cancellationToken);
            return new GetSettingsResponse(MapSettings(settings));
        }

        var app = await ResolveApplicationAsync(request.ProjectId, cancellationToken);
        return new GetSettingsResponse(MapSettings(app));
    }

    public async Task<UpdateSettingsResponse> UpdateSettingsAsync(
        UpdateSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ProjectId))
        {
            var settingsStore = CreateUserSettingsStore();
            var current = await settingsStore.LoadAsync(cancellationToken);
            var nextSettings = BuildUpdatedSettings(current, request);
            await settingsStore.SaveAsync(nextSettings, cancellationToken);
            return new UpdateSettingsResponse(MapSettings(nextSettings));
        }

        var app = await ResolveApplicationAsync(request.ProjectId, cancellationToken);
        var nextProjectSettings = BuildUpdatedSettings(app.AppState.Settings, request);
        app.AppStateStore.SetState(state => ClawSharpAppStateMutations.WithSettings(state, nextProjectSettings));
        await app.SettingsStore.SaveAsync(nextProjectSettings, cancellationToken);
        return new UpdateSettingsResponse(MapSettings(app));
    }

    public async Task<ValidateProviderConfigResponse> ValidateProviderConfigAsync(
        ValidateProviderConfigRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Provider))
        {
            throw new AgentHostException("invalid_request", "provider is required.");
        }

        var provider = request.Provider.Trim().ToLowerInvariant();
        var settings = await ResolveSettingsForValidationAsync(request.ProjectId, cancellationToken);
        var validationTarget = BuildValidationTarget(settings, request);
        var errors = new List<string>();
        var warnings = new List<string>();

        switch (provider)
        {
            case "anthropic":
                ValidateAnthropicConfig(validationTarget.Settings, validationTarget.Model, errors);
                break;
            case "openai":
                ValidateOpenAiConfig(validationTarget.Settings, validationTarget.Model, errors);
                break;
            case "codex":
                ValidateCodexConfig(validationTarget.Settings, validationTarget.Model, errors, warnings);
                break;
            case "gemini":
                ValidateGeminiConfig(validationTarget.Settings, validationTarget.Model, errors);
                break;
            case "github":
                ValidateGitHubConfig(validationTarget.Settings, validationTarget.Model, errors);
                break;
            case "ollama":
                warnings.Add("Ollama assumes a local server is reachable at http://localhost:11434/v1.");
                break;
            default:
                warnings.Add($"No desktop validation rule exists yet for provider '{provider}'.");
                break;
        }

        if (request.LiveCheck &&
            errors.Count == 0 &&
            ProviderOptions.Any(item => string.Equals(item.Id, provider, StringComparison.OrdinalIgnoreCase)))
        {
            var liveResult = await _liveValidationService.ValidateAsync(
                provider,
                validationTarget.Model,
                validationTarget.Settings,
                cancellationToken);
            errors.AddRange(liveResult.Errors);
            warnings.AddRange(liveResult.Warnings);
        }

        return new ValidateProviderConfigResponse(
            new ProviderValidationDto(request.Provider, errors.Count == 0, errors, warnings));
    }

    private async Task<Infrastructure.ClawSharpApplication> ResolveApplicationAsync(
        string? projectId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            var recentProject = (await _recentProjectStore.ListAsync(cancellationToken)).FirstOrDefault();
            if (recentProject is null)
            {
                throw new AgentHostException("project_not_found", "No project is currently open.");
            }

            return await _applicationRegistry.GetOrCreateAsync(recentProject.Path, cancellationToken);
        }

        var project = await _recentProjectStore.FindByIdAsync(projectId, cancellationToken);
        if (project is null)
        {
            throw new AgentHostException("project_not_found", $"Project '{projectId}' is not known to AgentHost yet.");
        }

        return await _applicationRegistry.GetOrCreateAsync(project.Path, cancellationToken);
    }

    private RuntimeSettingsDto MapSettings(Infrastructure.ClawSharpApplication app)
    {
        var state = app.AppStateStore.GetState();
        return MapSettings(
            state.Settings,
            state.SettingsIssues.Select(issue => $"{issue.File}: {issue.Message}"),
            state.MainLoopModel);
    }

    private RuntimeSettingsDto MapSettings(
        ClawSharpSettings settings,
        IEnumerable<string>? settingsIssues = null,
        string? mainLoopModel = null)
    {
        var runtime = ProviderRuntimeResolver.Resolve(settings, mainLoopModel ?? settings.Runtime.Model);
        return new RuntimeSettingsDto(
            runtime.Provider.ToString().ToLowerInvariant(),
            runtime.ResolvedModel,
            settings.Runtime.FallbackModel,
            settings.Runtime.PermissionMode.ToString(),
            settings.Runtime.EnableTelemetry,
            settings.Runtime.FileCheckpointingEnabled,
            runtime.BaseUrl,
            runtime.Transport.ToString(),
            ClaudeConfigPaths.GetUserSettingsFilePath(),
            (settingsIssues ?? Enumerable.Empty<string>()).ToArray(),
            MapCredentialState(settings, runtime.Provider, runtime.RequestedModel, runtime.ResolvedModel),
            HasAnyConfiguredProviderCredential(settings));
    }

    private async Task<ClawSharpSettings> ResolveSettingsForValidationAsync(
        string? projectId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return await LoadUserSettingsAsync(cancellationToken);
        }

        var app = await ResolveApplicationAsync(projectId, cancellationToken);
        return app.AppStateStore.GetState().Settings;
    }

    private async Task<ClawSharpSettings> LoadUserSettingsAsync(CancellationToken cancellationToken)
    {
        var settingsStore = CreateUserSettingsStore();
        return await settingsStore.LoadAsync(cancellationToken);
    }

    private static ISettingsStore CreateUserSettingsStore()
    {
        return new JsonSettingsStore(ClaudeConfigPaths.GetUserSettingsFilePath());
    }

    private static ClawSharpSettings BuildUpdatedSettings(
        ClawSharpSettings current,
        UpdateSettingsRequest request)
    {
        var currentRuntime = ProviderRuntimeResolver.Resolve(current, current.Runtime.Model);
        var nextModel = string.IsNullOrWhiteSpace(request.Model) ? current.Runtime.Model : request.Model.Trim();
        var nextRuntime = new RuntimeSettings
        {
            PermissionMode = current.Runtime.PermissionMode,
            Model = nextModel,
            FallbackModel = request.FallbackModel ?? current.Runtime.FallbackModel,
            EnableTelemetry = request.EnableTelemetry ?? current.Runtime.EnableTelemetry,
            FileCheckpointingEnabled = request.FileCheckpointingEnabled ?? current.Runtime.FileCheckpointingEnabled,
            AutoMemoryEnabled = current.Runtime.AutoMemoryEnabled,
            AutoMemoryDirectory = current.Runtime.AutoMemoryDirectory
        };
        var agentModels = new Dictionary<string, AgentModelConnection>(current.AgentModels, StringComparer.Ordinal);
        var agentRouting = new Dictionary<string, string>(current.AgentRouting, StringComparer.Ordinal);
        var nextSettings = new ClawSharpSettings
        {
            Runtime = nextRuntime,
            Terminal = current.Terminal,
            Sandbox = current.Sandbox,
            ClaudeApiKey = current.ClaudeApiKey,
            SkipAutoPermissionPrompt = current.SkipAutoPermissionPrompt,
            UseAutoModeDuringPlan = current.UseAutoModeDuringPlan,
            ApiKeyHelper = current.ApiKeyHelper,
            AwsCredentialExport = current.AwsCredentialExport,
            AwsAuthRefresh = current.AwsAuthRefresh,
            Agent = current.Agent,
            Attribution = current.Attribution,
            Permissions = current.Permissions,
            AllowManagedPermissionRulesOnly = current.AllowManagedPermissionRulesOnly,
            Hooks = current.Hooks,
            DisableAllHooks = current.DisableAllHooks,
            AllowManagedHooksOnly = current.AllowManagedHooksOnly,
            ForceLoginOrgUUID = current.ForceLoginOrgUUID,
            OtelHeadersHelper = current.OtelHeadersHelper,
            EnabledPlugins = current.EnabledPlugins,
            PluginConfigs = current.PluginConfigs,
            AgentModels = agentModels,
            AgentRouting = agentRouting
        };
        var nextProvider = string.IsNullOrWhiteSpace(request.Provider)
            ? ToProviderId(currentRuntime.Provider)
            : request.Provider.Trim().ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(nextProvider))
        {
            var providerError = ProviderFlagUtilities.ApplyProviderFlags(
                ["--provider", nextProvider, "--model", nextRuntime.Model]);
            if (!string.IsNullOrWhiteSpace(providerError))
            {
                throw new AgentHostException("invalid_provider", providerError);
            }
        }

        if (HasProviderSelectionUpdate(request))
        {
            nextSettings = ApplyProviderSelection(nextSettings, current, currentRuntime, nextProvider, nextModel);
        }

        if (HasCredentialUpdate(request))
        {
            nextSettings = ApplyCredentialUpdate(nextSettings, nextProvider, nextModel, request);
        }

        return nextSettings;
    }

    private ValidationTarget BuildValidationTarget(
        ClawSharpSettings settings,
        ValidateProviderConfigRequest request)
    {
        var provider = request.Provider.Trim().ToLowerInvariant();
        var model = ResolveValidationModel(provider, request.Model, settings);
        var existingConnection = ResolveConnection(settings, model, ProviderRuntimeResolver.ResolveModelAlias(model));
        var runtime = new RuntimeSettings
        {
            PermissionMode = settings.Runtime.PermissionMode,
            Model = model,
            FallbackModel = settings.Runtime.FallbackModel,
            EnableTelemetry = settings.Runtime.EnableTelemetry,
            FileCheckpointingEnabled = settings.Runtime.FileCheckpointingEnabled,
            AutoMemoryEnabled = settings.Runtime.AutoMemoryEnabled,
            AutoMemoryDirectory = settings.Runtime.AutoMemoryDirectory
        };
        var agentModels = new Dictionary<string, AgentModelConnection>(settings.AgentModels, StringComparer.Ordinal);
        var agentRouting = new Dictionary<string, string>(settings.AgentRouting, StringComparer.Ordinal);
        var claudeApiKey = settings.ClaudeApiKey;

        if (provider == "anthropic")
        {
            var resolvedAuthToken = request.AuthToken ??
                                    existingConnection?.AuthToken ??
                                    ResolveAnthropicAuthToken();
            var resolvedApiKey = request.ApiKey ??
                                 existingConnection?.ApiKey ??
                                 settings.ClaudeApiKey ??
                                 Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ??
                                 Environment.GetEnvironmentVariable("CLAUDE_API_KEY");
            claudeApiKey = string.IsNullOrWhiteSpace(resolvedApiKey) ? null : resolvedApiKey.Trim();
            agentModels[model] = CloneConnection(
                existingConnection,
                provider: "anthropic",
                baseUrl: existingConnection?.BaseUrl ?? ProviderRuntimeResolver.DefaultAnthropicBaseUrl,
                apiKey: resolvedApiKey,
                authToken: resolvedAuthToken,
                accountId: request.AccountId ?? existingConnection?.AccountId);
        }
        else
        {
            agentModels[model] = BuildValidationConnection(provider, model, existingConnection, request);
        }

        return new ValidationTarget(
            model,
            CloneSettings(settings, runtime, claudeApiKey, agentModels, agentRouting));
    }

    private static bool HasCredentialUpdate(UpdateSettingsRequest request)
    {
        return request.ApiKey is not null ||
               request.AuthToken is not null ||
               request.AccountId is not null ||
               request.ClearApiKey == true ||
               request.ClearAuthToken == true ||
               request.ClearAccountId == true ||
               request.UseExternalCredential is not null;
    }

    private static bool HasProviderSelectionUpdate(UpdateSettingsRequest request)
    {
        return !string.IsNullOrWhiteSpace(request.Provider) ||
               !string.IsNullOrWhiteSpace(request.Model);
    }

    private static ClawSharpSettings ApplyProviderSelection(
        ClawSharpSettings nextSettings,
        ClawSharpSettings currentSettings,
        ProviderRuntimeConfig currentRuntime,
        string provider,
        string model)
    {
        var agentModels = new Dictionary<string, AgentModelConnection>(nextSettings.AgentModels, StringComparer.Ordinal);
        var agentRouting = new Dictionary<string, string>(nextSettings.AgentRouting, StringComparer.Ordinal);
        agentModels.TryGetValue(model, out var targetConnection);

        var currentConnection = ResolveConnection(
            currentSettings,
            currentRuntime.RequestedModel,
            currentRuntime.ResolvedModel);
        var normalizedProvider = NormalizeStoredProviderId(provider);
        var preservedConnection = IsConnectionForProvider(targetConnection, normalizedProvider)
            ? targetConnection
            : string.Equals(ToProviderId(currentRuntime.Provider), provider, StringComparison.OrdinalIgnoreCase)
                ? currentConnection
                : null;

        switch (provider)
        {
            case "anthropic":
                if (preservedConnection is not null &&
                    (!string.IsNullOrWhiteSpace(preservedConnection.BaseUrl) ||
                     !string.IsNullOrWhiteSpace(preservedConnection.AuthToken) ||
                     !string.IsNullOrWhiteSpace(preservedConnection.AccountId)))
                {
                    agentModels[model] = CloneConnection(
                        preservedConnection,
                        provider: "anthropic",
                        baseUrl: preservedConnection.BaseUrl ?? ProviderRuntimeResolver.DefaultAnthropicBaseUrl,
                        apiKey: nextSettings.ClaudeApiKey,
                        authToken: preservedConnection.AuthToken,
                        accountId: preservedConnection.AccountId);
                }
                else
                {
                    agentModels.Remove(model);
                }

                agentRouting.Remove("default");
                break;
            case "ollama":
                agentModels[model] = CloneConnection(
                    preservedConnection,
                    provider: "openai",
                    baseUrl: preservedConnection?.BaseUrl ?? "http://localhost:11434/v1",
                    apiKey: preservedConnection?.ApiKey ?? "ollama",
                    authToken: null,
                    accountId: null);
                agentRouting["default"] = model;
                break;
            default:
                agentModels[model] = CloneConnection(
                    preservedConnection,
                    provider: normalizedProvider,
                    baseUrl: preservedConnection?.BaseUrl ?? ResolveDefaultBaseUrl(provider),
                    apiKey: preservedConnection?.ApiKey,
                    authToken: preservedConnection?.AuthToken,
                    accountId: preservedConnection?.AccountId);
                agentRouting["default"] = model;
                break;
        }

        return CloneSettings(nextSettings, nextSettings.Runtime, nextSettings.ClaudeApiKey, agentModels, agentRouting);
    }

    private static bool IsConnectionForProvider(AgentModelConnection? connection, string provider)
    {
        return connection is not null &&
               string.Equals(connection.Provider, provider, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeStoredProviderId(string provider)
    {
        return provider switch
        {
            "ollama" => "openai",
            _ => provider
        };
    }

    private static ClawSharpSettings ApplyCredentialUpdate(
        ClawSharpSettings settings,
        string provider,
        string model,
        UpdateSettingsRequest request)
    {
        var agentModels = new Dictionary<string, AgentModelConnection>(settings.AgentModels, StringComparer.Ordinal);
        var agentRouting = new Dictionary<string, string>(settings.AgentRouting, StringComparer.Ordinal);
        var claudeApiKey = settings.ClaudeApiKey;
        agentModels.TryGetValue(model, out var existingConnection);

        var currentApiKey = request.ClearApiKey == true ? null : request.ApiKey ?? existingConnection?.ApiKey;
        var currentAuthToken = request.ClearAuthToken == true ? null : request.AuthToken ?? existingConnection?.AuthToken;
        var currentAccountId = request.ClearAccountId == true ? null : request.AccountId ?? existingConnection?.AccountId;

        switch (provider)
        {
            case "anthropic":
                claudeApiKey = request.ClearApiKey == true ? null : request.ApiKey ?? claudeApiKey;

                if (ShouldPersistConnection(settings, model, request, currentApiKey, currentAuthToken, currentAccountId))
                {
                    agentModels[model] = CloneConnection(
                        existingConnection,
                        provider: "anthropic",
                        baseUrl: existingConnection?.BaseUrl ?? ProviderRuntimeResolver.DefaultAnthropicBaseUrl,
                        apiKey: currentApiKey,
                        authToken: currentAuthToken,
                        accountId: currentAccountId);
                }
                else if (existingConnection is not null)
                {
                    agentModels.Remove(model);
                }

                agentRouting.Remove("default");
                break;
            case "codex":
                agentModels[model] = CloneConnection(
                    existingConnection,
                    provider: "codex",
                    baseUrl: existingConnection?.BaseUrl ?? ProviderRuntimeResolver.DefaultCodexBaseUrl,
                    apiKey: currentApiKey,
                    authToken: currentAuthToken,
                    accountId: currentAccountId,
                    useExternalCredential: request.UseExternalCredential ?? existingConnection?.UseExternalCredential ?? false);
                agentRouting["default"] = model;
                break;
            case "ollama":
                agentModels[model] = CloneConnection(
                    existingConnection,
                    provider: "openai",
                    baseUrl: existingConnection?.BaseUrl ?? "http://localhost:11434/v1",
                    apiKey: existingConnection?.ApiKey ?? "ollama",
                    authToken: null,
                    accountId: null);
                agentRouting["default"] = model;
                break;
            default:
                agentModels[model] = CloneConnection(
                    existingConnection,
                    provider: provider switch
                    {
                        "ollama" => "openai",
                        _ => provider
                    },
                    baseUrl: existingConnection?.BaseUrl ?? ResolveDefaultBaseUrl(provider),
                    apiKey: currentApiKey,
                    authToken: currentAuthToken,
                    accountId: currentAccountId);
                agentRouting["default"] = model;
                break;
        }

        return CloneSettings(settings, settings.Runtime, claudeApiKey, agentModels, agentRouting);
    }

    private static bool ShouldPersistConnection(
        ClawSharpSettings settings,
        string model,
        UpdateSettingsRequest request,
        string? apiKey,
        string? authToken,
        string? accountId)
    {
        return settings.AgentModels.ContainsKey(model) ||
               request.AuthToken is not null ||
               request.AccountId is not null ||
               request.ClearAuthToken == true ||
               request.ClearAccountId == true ||
               !string.IsNullOrWhiteSpace(apiKey) ||
               !string.IsNullOrWhiteSpace(authToken) ||
               !string.IsNullOrWhiteSpace(accountId);
    }

    private static AgentModelConnection CloneConnection(
        AgentModelConnection? existing,
        string provider,
        string baseUrl,
        string? apiKey,
        string? authToken,
        string? accountId,
        bool? useExternalCredential = null)
    {
        return new AgentModelConnection
        {
            Provider = provider,
            BaseUrl = baseUrl,
            ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim(),
            AuthToken = string.IsNullOrWhiteSpace(authToken) ? null : authToken.Trim(),
            AccountId = string.IsNullOrWhiteSpace(accountId) ? null : accountId.Trim(),
            ApiVersion = existing?.ApiVersion,
            UseExternalCredential = useExternalCredential ?? existing?.UseExternalCredential ?? false,
            Headers = existing?.Headers ??
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
    }

    private static ProviderCredentialStateDto MapCredentialState(
        ClawSharpSettings settings,
        ApiProviderKind provider,
        string requestedModel,
        string resolvedModel)
    {
        var connection = ResolveConnection(settings, requestedModel, resolvedModel);
        return provider switch
        {
            ApiProviderKind.Anthropic => new ProviderCredentialStateDto(
                !string.IsNullOrWhiteSpace(settings.ClaudeApiKey) || !string.IsNullOrWhiteSpace(connection?.ApiKey),
                !string.IsNullOrWhiteSpace(connection?.AuthToken),
                connection?.AccountId,
                DetermineCredentialSource(settings, provider, connection),
                false,
                null),
            ApiProviderKind.Codex => MapCodexCredentialState(settings, connection),
            _ => new ProviderCredentialStateDto(
                !string.IsNullOrWhiteSpace(connection?.ApiKey),
                !string.IsNullOrWhiteSpace(connection?.AuthToken),
                connection?.AccountId,
                DetermineCredentialSource(settings, provider, connection),
                false,
                null)
        };
    }

    private static ProviderCredentialStateDto MapCodexCredentialState(
        ClawSharpSettings settings,
        AgentModelConnection? connection)
    {
        var hasSavedApiKey = !string.IsNullOrWhiteSpace(connection?.ApiKey);
        var hasSavedAuthToken = !string.IsNullOrWhiteSpace(connection?.AuthToken);
        var hasSavedAccountId = !string.IsNullOrWhiteSpace(connection?.AccountId);
        var externalCredentials = ProviderRuntimeResolver.ResolveCodexCredentials();
        var hasExternalAuthFile = !string.IsNullOrWhiteSpace(externalCredentials.AuthPath) &&
                                  File.Exists(externalCredentials.AuthPath);
        var source = connection?.UseExternalCredential == true && hasExternalAuthFile
            ? "external"
            : hasSavedApiKey || hasSavedAuthToken || hasSavedAccountId
                ? "saved"
                : hasExternalAuthFile
                    ? "external"
                    : "none";

        return new ProviderCredentialStateDto(
            hasSavedApiKey,
            hasSavedAuthToken,
            connection?.AccountId,
            source,
            hasExternalAuthFile,
            externalCredentials.AuthPath);
    }

    private static AgentModelConnection? ResolveConnection(
        ClawSharpSettings settings,
        string? requestedModel,
        string? resolvedModel)
    {
        if (!string.IsNullOrWhiteSpace(requestedModel) &&
            settings.AgentModels.TryGetValue(requestedModel.Trim(), out var requestedConnection))
        {
            return requestedConnection;
        }

        if (!string.IsNullOrWhiteSpace(resolvedModel) &&
            settings.AgentModels.TryGetValue(resolvedModel.Trim(), out var resolvedConnection))
        {
            return resolvedConnection;
        }

        return null;
    }

    private void ValidateAnthropicConfig(
        ClawSharpSettings settings,
        string? model,
        List<string> errors)
    {
        var connection = ResolveConnection(settings, model, model);
        if (HasAnyEnvironmentVariable("ANTHROPIC_API_KEY", "CLAUDE_API_KEY") ||
            !string.IsNullOrWhiteSpace(settings.ClaudeApiKey) ||
            !string.IsNullOrWhiteSpace(connection?.ApiKey) ||
            !string.IsNullOrWhiteSpace(connection?.AuthToken) ||
            !string.IsNullOrWhiteSpace(ResolveAnthropicAuthToken()))
        {
            return;
        }

        errors.Add("Anthropic API key or Claude auth token is not configured for provider 'anthropic'.");
    }

    private static void ValidateOpenAiConfig(
        ClawSharpSettings settings,
        string? model,
        List<string> errors)
    {
        var connection = ResolveConnection(settings, model, model);
        if (HasAnyEnvironmentVariable("OPENAI_API_KEY") ||
            !string.IsNullOrWhiteSpace(connection?.ApiKey) ||
            !string.IsNullOrWhiteSpace(connection?.AuthToken))
        {
            return;
        }

        errors.Add("OpenAI API key is not configured for provider 'openai'.");
    }

    private static void ValidateCodexConfig(
        ClawSharpSettings settings,
        string? model,
        List<string> errors,
        List<string> warnings)
    {
        var connection = ResolveConnection(settings, model, model);
        var hasSavedCredential =
            !string.IsNullOrWhiteSpace(connection?.ApiKey) ||
            !string.IsNullOrWhiteSpace(connection?.AuthToken);
        var externalCredentials = ProviderRuntimeResolver.ResolveCodexCredentials();
        var hasExternalCredential =
            HasAnyEnvironmentVariable("CODEX_API_KEY", "OPENAI_API_KEY") ||
            !string.IsNullOrWhiteSpace(externalCredentials.ApiKey);
        var hasCredential = hasSavedCredential || hasExternalCredential;
        if (!hasCredential)
        {
            errors.Add("Codex access token is not configured for provider 'codex'.");
            return;
        }

        var accountId = hasSavedCredential
            ? connection?.AccountId
            : externalCredentials.AccountId;
        if (string.IsNullOrWhiteSpace(accountId))
        {
            warnings.Add("Codex account ID is not configured. Account-specific requests may fail.");
        }
    }

    private static string DetermineCredentialSource(
        ClawSharpSettings settings,
        ApiProviderKind provider,
        AgentModelConnection? connection)
    {
        return provider switch
        {
            ApiProviderKind.Anthropic when !string.IsNullOrWhiteSpace(settings.ClaudeApiKey) ||
                                           !string.IsNullOrWhiteSpace(connection?.ApiKey) ||
                                           !string.IsNullOrWhiteSpace(connection?.AuthToken) => "saved",
            _ when !string.IsNullOrWhiteSpace(connection?.ApiKey) ||
                     !string.IsNullOrWhiteSpace(connection?.AuthToken) ||
                     !string.IsNullOrWhiteSpace(connection?.AccountId) => "saved",
            _ => "none"
        };
    }

    private static void ValidateGeminiConfig(
        ClawSharpSettings settings,
        string? model,
        List<string> errors)
    {
        var connection = ResolveConnection(settings, model, model);
        if (HasAnyEnvironmentVariable("GEMINI_API_KEY", "GOOGLE_API_KEY", "GEMINI_ACCESS_TOKEN") ||
            !string.IsNullOrWhiteSpace(connection?.ApiKey) ||
            !string.IsNullOrWhiteSpace(connection?.AuthToken))
        {
            return;
        }

        errors.Add("Gemini API key or access token is not configured for provider 'gemini'.");
    }

    private static void ValidateGitHubConfig(
        ClawSharpSettings settings,
        string? model,
        List<string> errors)
    {
        var connection = ResolveConnection(settings, model, model);
        if (HasAnyEnvironmentVariable("GITHUB_TOKEN", "GH_TOKEN") ||
            !string.IsNullOrWhiteSpace(connection?.AuthToken) ||
            !string.IsNullOrWhiteSpace(connection?.ApiKey))
        {
            return;
        }

        errors.Add("GitHub token is not configured for provider 'github'.");
    }

    private bool HasAnyConfiguredProviderCredential(ClawSharpSettings settings)
    {
        return HasAnthropicCredential(settings) ||
               HasProviderConnectionCredential(settings, "openai", "github", "gemini", "codex") ||
               ProviderRuntimeResolver.ResolveCodexCredentials().ApiKey is not null ||
               HasAnyEnvironmentVariable(
                   "OPENAI_API_KEY",
                   "GITHUB_TOKEN",
                   "GH_TOKEN",
                   "GEMINI_API_KEY",
                   "GOOGLE_API_KEY",
                   "GEMINI_ACCESS_TOKEN");
    }

    private bool HasAnthropicCredential(ClawSharpSettings settings)
    {
        if (HasAnyEnvironmentVariable(
                "ANTHROPIC_API_KEY",
                "CLAUDE_API_KEY",
                "ANTHROPIC_AUTH_TOKEN",
                "CLAUDE_CODE_OAUTH_TOKEN") ||
            !string.IsNullOrWhiteSpace(settings.ClaudeApiKey))
        {
            return true;
        }

        if (settings.AgentModels.Values.Any(
                connection => string.Equals(connection.Provider, "anthropic", StringComparison.OrdinalIgnoreCase) &&
                              (!string.IsNullOrWhiteSpace(connection.ApiKey) ||
                               !string.IsNullOrWhiteSpace(connection.AuthToken))))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(ResolveAnthropicAuthToken());
    }

    private static bool HasProviderConnectionCredential(
        ClawSharpSettings settings,
        params string[] providerIds)
    {
        return settings.AgentModels.Values.Any(
            connection => providerIds.Any(
                              providerId => string.Equals(connection.Provider, providerId, StringComparison.OrdinalIgnoreCase)) &&
                          (!string.IsNullOrWhiteSpace(connection.ApiKey) ||
                           !string.IsNullOrWhiteSpace(connection.AuthToken) ||
                           !string.IsNullOrWhiteSpace(connection.AccountId)));
    }

    private static string ResolveValidationModel(
        string provider,
        string? requestedModel,
        ClawSharpSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(requestedModel))
        {
            return requestedModel.Trim();
        }

        var option = ProviderOptions.FirstOrDefault(item => string.Equals(item.Id, provider, StringComparison.OrdinalIgnoreCase));
        return option?.DefaultModel ?? settings.Runtime.Model;
    }

    private static AgentModelConnection BuildValidationConnection(
        string provider,
        string model,
        AgentModelConnection? existingConnection,
        ValidateProviderConfigRequest request)
    {
        return provider switch
        {
            "openai" => CloneConnection(
                existingConnection,
                provider: "openai",
                baseUrl: existingConnection?.BaseUrl ??
                         Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ??
                         Environment.GetEnvironmentVariable("OPENAI_API_BASE") ??
                         ProviderRuntimeResolver.DefaultOpenAiBaseUrl,
                apiKey: request.ApiKey ??
                        existingConnection?.ApiKey ??
                        Environment.GetEnvironmentVariable("OPENAI_API_KEY"),
                authToken: request.AuthToken ?? existingConnection?.AuthToken,
                accountId: request.AccountId ?? existingConnection?.AccountId),
            "gemini" => BuildGeminiValidationConnection(existingConnection, request),
            "github" => CloneConnection(
                existingConnection,
                provider: "github",
                baseUrl: existingConnection?.BaseUrl ?? ProviderRuntimeResolver.DefaultGitHubModelsBaseUrl,
                apiKey: request.ApiKey ?? existingConnection?.ApiKey,
                authToken: request.AuthToken ??
                           existingConnection?.AuthToken ??
                           Environment.GetEnvironmentVariable("GITHUB_TOKEN") ??
                           Environment.GetEnvironmentVariable("GH_TOKEN"),
                accountId: request.AccountId ?? existingConnection?.AccountId),
            "codex" => BuildCodexValidationConnection(existingConnection, request),
            "ollama" => CloneConnection(
                existingConnection,
                provider: "openai",
                baseUrl: existingConnection?.BaseUrl ?? "http://localhost:11434/v1",
                apiKey: request.ApiKey ?? existingConnection?.ApiKey ?? "ollama",
                authToken: null,
                accountId: null),
            _ => CloneConnection(
                existingConnection,
                provider: provider,
                baseUrl: existingConnection?.BaseUrl ?? ResolveDefaultBaseUrl(provider),
                apiKey: request.ApiKey ?? existingConnection?.ApiKey,
                authToken: request.AuthToken ?? existingConnection?.AuthToken,
                accountId: request.AccountId ?? existingConnection?.AccountId)
        };
    }

    private static AgentModelConnection BuildGeminiValidationConnection(
        AgentModelConnection? existingConnection,
        ValidateProviderConfigRequest request)
    {
        var credential = ProviderRuntimeResolver.ResolveGeminiCredential();
        var headers = existingConnection?.Headers is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(existingConnection.Headers, StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(credential?.ProjectId))
        {
            headers["x-goog-user-project"] = credential.ProjectId;
        }

        return new AgentModelConnection
        {
            Provider = "gemini",
            BaseUrl = existingConnection?.BaseUrl ?? ProviderRuntimeResolver.DefaultGeminiBaseUrl,
            ApiKey = string.IsNullOrWhiteSpace(request.ApiKey)
                ? existingConnection?.ApiKey ?? (credential is { Kind: "api-key" } ? credential.Credential : null)
                : request.ApiKey.Trim(),
            AuthToken = string.IsNullOrWhiteSpace(request.AuthToken)
                ? existingConnection?.AuthToken ?? (credential is { Kind: not "api-key" } ? credential?.Credential : null)
                : request.AuthToken.Trim(),
            AccountId = string.IsNullOrWhiteSpace(request.AccountId) ? existingConnection?.AccountId : request.AccountId.Trim(),
            ApiVersion = existingConnection?.ApiVersion,
            Headers = headers
        };
    }

    private static AgentModelConnection BuildCodexValidationConnection(
        AgentModelConnection? existingConnection,
        ValidateProviderConfigRequest request)
    {
        var externalCredentials = ProviderRuntimeResolver.ResolveCodexCredentials();
        var useExternalCredential = request.UseExternalCredential ?? existingConnection?.UseExternalCredential ?? false;
        var apiKey = useExternalCredential
            ? externalCredentials.ApiKey
            : request.ApiKey ?? existingConnection?.ApiKey ?? existingConnection?.AuthToken ?? externalCredentials.ApiKey;
        var accountId = useExternalCredential
            ? request.AccountId ?? externalCredentials.AccountId
            : request.AccountId ?? existingConnection?.AccountId ?? externalCredentials.AccountId;

        return CloneConnection(
            existingConnection,
            provider: "codex",
            baseUrl: existingConnection?.BaseUrl ?? ProviderRuntimeResolver.DefaultCodexBaseUrl,
            apiKey: apiKey,
            authToken: null,
            accountId: accountId,
            useExternalCredential: useExternalCredential);
    }

    private string? ResolveAnthropicAuthToken()
    {
        var oauthTokenSource = new ClaudeAiOAuthTokenSource();
        return Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN") ??
               Environment.GetEnvironmentVariable("CLAUDE_CODE_OAUTH_TOKEN") ??
               oauthTokenSource.ReadToken() ??
               _secureStorage.Read()?.ClaudeAiOauth?.AccessToken;
    }

    private static bool HasAnyEnvironmentVariable(params string[] variableNames)
    {
        return variableNames.Any(name => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)));
    }

    private sealed record ValidationTarget(
        string Model,
        ClawSharpSettings Settings);

    private static string ToProviderId(ApiProviderKind provider)
    {
        return provider switch
        {
            ApiProviderKind.OpenAi => "openai",
            ApiProviderKind.GitHub => "github",
            ApiProviderKind.Codex => "codex",
            ApiProviderKind.Gemini => "gemini",
            ApiProviderKind.Bedrock => "bedrock",
            ApiProviderKind.Vertex => "vertex",
            ApiProviderKind.Foundry => "foundry",
            _ => "anthropic"
        };
    }

    private static string ResolveDefaultBaseUrl(string provider)
    {
        return provider switch
        {
            "anthropic" or "bedrock" or "vertex" or "foundry" => ProviderRuntimeResolver.DefaultAnthropicBaseUrl,
            "gemini" => ProviderRuntimeResolver.DefaultGeminiBaseUrl,
            "github" => ProviderRuntimeResolver.DefaultGitHubModelsBaseUrl,
            "codex" => ProviderRuntimeResolver.DefaultCodexBaseUrl,
            "ollama" => "http://localhost:11434/v1",
            _ => ProviderRuntimeResolver.DefaultOpenAiBaseUrl
        };
    }

    private static ClawSharpSettings CloneSettings(
        ClawSharpSettings source,
        RuntimeSettings runtime,
        string? claudeApiKey,
        IReadOnlyDictionary<string, AgentModelConnection> agentModels,
        IReadOnlyDictionary<string, string> agentRouting)
    {
        return new ClawSharpSettings
        {
            Runtime = runtime,
            Terminal = source.Terminal,
            Sandbox = source.Sandbox,
            ClaudeApiKey = claudeApiKey,
            SkipAutoPermissionPrompt = source.SkipAutoPermissionPrompt,
            UseAutoModeDuringPlan = source.UseAutoModeDuringPlan,
            ApiKeyHelper = source.ApiKeyHelper,
            AwsCredentialExport = source.AwsCredentialExport,
            AwsAuthRefresh = source.AwsAuthRefresh,
            Agent = source.Agent,
            Attribution = source.Attribution,
            Permissions = source.Permissions,
            AllowManagedPermissionRulesOnly = source.AllowManagedPermissionRulesOnly,
            Hooks = source.Hooks,
            DisableAllHooks = source.DisableAllHooks,
            AllowManagedHooksOnly = source.AllowManagedHooksOnly,
            ForceLoginOrgUUID = source.ForceLoginOrgUUID,
            OtelHeadersHelper = source.OtelHeadersHelper,
            EnabledPlugins = source.EnabledPlugins,
            PluginConfigs = source.PluginConfigs,
            AgentModels = agentModels,
            AgentRouting = agentRouting
        };
    }
}
