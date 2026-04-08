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

    public ProviderCatalogService(
        WorkspaceApplicationRegistry applicationRegistry,
        RecentProjectStore recentProjectStore)
    {
        _applicationRegistry = applicationRegistry;
        _recentProjectStore = recentProjectStore;
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
        var app = await ResolveApplicationAsync(request.ProjectId, cancellationToken);
        return new GetSettingsResponse(MapSettings(app));
    }

    public async Task<UpdateSettingsResponse> UpdateSettingsAsync(
        UpdateSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        var app = await ResolveApplicationAsync(request.ProjectId, cancellationToken);
        var current = app.AppState.Settings;
        var nextRuntime = new RuntimeSettings
        {
            PermissionMode = current.Runtime.PermissionMode,
            Model = string.IsNullOrWhiteSpace(request.Model) ? current.Runtime.Model : request.Model.Trim(),
            FallbackModel = request.FallbackModel ?? current.Runtime.FallbackModel,
            EnableTelemetry = request.EnableTelemetry ?? current.Runtime.EnableTelemetry,
            FileCheckpointingEnabled = request.FileCheckpointingEnabled ?? current.Runtime.FileCheckpointingEnabled,
            AutoMemoryEnabled = current.Runtime.AutoMemoryEnabled,
            AutoMemoryDirectory = current.Runtime.AutoMemoryDirectory
        };
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
            AgentModels = current.AgentModels,
            AgentRouting = current.AgentRouting
        };

        if (!string.IsNullOrWhiteSpace(request.Provider))
        {
            var providerError = ProviderFlagUtilities.ApplyProviderFlags(
                ["--provider", request.Provider.Trim(), "--model", nextRuntime.Model]);
            if (!string.IsNullOrWhiteSpace(providerError))
            {
                throw new AgentHostException("invalid_provider", providerError);
            }
        }

        app.AppStateStore.SetState(state => ClawSharpAppStateMutations.WithSettings(state, nextSettings));
        await app.SettingsStore.SaveAsync(nextSettings, cancellationToken);
        return new UpdateSettingsResponse(MapSettings(app));
    }

    public Task<ValidateProviderConfigResponse> ValidateProviderConfigAsync(
        ValidateProviderConfigRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Provider))
        {
            throw new AgentHostException("invalid_request", "provider is required.");
        }

        var provider = request.Provider.Trim().ToLowerInvariant();
        var errors = new List<string>();
        var warnings = new List<string>();

        switch (provider)
        {
            case "anthropic":
                ValidateAny(errors, ["ANTHROPIC_API_KEY", "CLAUDE_API_KEY"], "Anthropic API key", provider);
                break;
            case "openai":
            case "codex":
                ValidateAny(errors, ["OPENAI_API_KEY"], "OpenAI API key", provider);
                break;
            case "gemini":
                ValidateAny(errors, ["GEMINI_API_KEY"], "Gemini API key", provider);
                break;
            case "github":
                ValidateAny(errors, ["GITHUB_TOKEN"], "GitHub token", provider);
                break;
            case "ollama":
                warnings.Add("Ollama assumes a local server is reachable at http://localhost:11434/v1.");
                break;
            default:
                warnings.Add($"No desktop validation rule exists yet for provider '{provider}'.");
                break;
        }

        return Task.FromResult(new ValidateProviderConfigResponse(
            new ProviderValidationDto(request.Provider, errors.Count == 0, errors, warnings)));
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

    private static RuntimeSettingsDto MapSettings(Infrastructure.ClawSharpApplication app)
    {
        var state = app.AppStateStore.GetState();
        var settings = state.Settings;
        var runtime = ProviderRuntimeResolver.Resolve(settings, state.MainLoopModel ?? settings.Runtime.Model);
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
            state.SettingsIssues.Select(issue => $"{issue.File}: {issue.Message}").ToArray());
    }

    private static void ValidateAny(List<string> errors, IReadOnlyList<string> variableNames, string label, string provider)
    {
        if (variableNames.Any(name => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))))
        {
            return;
        }

        errors.Add($"{label} is not configured for provider '{provider}'.");
    }
}
