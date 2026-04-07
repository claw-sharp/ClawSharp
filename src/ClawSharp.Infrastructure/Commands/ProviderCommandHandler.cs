using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class ProviderCommandHandler : ICommandHandler
{
    private static readonly string[] InteractiveProviders =
    [
        "anthropic",
        "openai",
        "gemini",
        "github",
        "bedrock",
        "vertex",
        "foundry",
        "codex",
        "ollama"
    ];

    public CommandDescriptor Descriptor { get; } =
        new(
            "provider",
            "Configure the active model provider",
            "/provider [anthropic|openai|gemini|github|bedrock|vertex|foundry|codex|ollama] [model] [base-url]",
            IsInteractive: true);

    public async Task<CommandResult> ExecuteAsync(
        string input,
        CommandExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var currentState = context.AppStateStore.GetState();
        var currentSettings = currentState.Settings;
        var currentRuntime = ProviderRuntimeResolver.Resolve(
            currentSettings,
            currentState.MainLoopModel ?? currentSettings.Runtime.Model);
        var arguments = SplitArguments(input);

        if (arguments.Count == 1)
        {
            if (context.InteractionService is null)
            {
                return new CommandResult(true, BuildStatusText(currentRuntime));
            }

            var interactiveResult = await RunInteractiveAsync(
                context.InteractionService,
                currentRuntime,
                cancellationToken);
            if (interactiveResult is null)
            {
                return new CommandResult(true, "Provider update cancelled.");
            }

            var updatedSettings = ApplySelection(currentSettings, interactiveResult.Value);
            ApplyProviderEnvironment(interactiveResult.Value);
            context.AppStateStore.SetState(state => ClawSharpAppStateMutations.WithSettings(state, updatedSettings));
            return new CommandResult(true, BuildUpdatedText(interactiveResult.Value, updatedSettings));
        }

        var parsedSelection = ParseArguments(arguments, currentRuntime);
        if (parsedSelection is null)
        {
            return new CommandResult(true, BuildUsageText(currentRuntime));
        }

        var nextSettings = ApplySelection(currentSettings, parsedSelection.Value);
        ApplyProviderEnvironment(parsedSelection.Value);
        context.AppStateStore.SetState(state => ClawSharpAppStateMutations.WithSettings(state, nextSettings));
        return new CommandResult(true, BuildUpdatedText(parsedSelection.Value, nextSettings));
    }

    private static async Task<ProviderSelection?> RunInteractiveAsync(
        IInteractionService interactionService,
        ProviderRuntimeConfig currentRuntime,
        CancellationToken cancellationToken)
    {
        var provider = await interactionService.SelectAsync(
            "Select a provider:",
            InteractiveProviders,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(provider))
        {
            return null;
        }

        provider = provider.Trim().ToLowerInvariant();
        var defaultModel = provider switch
        {
            "anthropic" => currentRuntime.Provider == ApiProviderKind.Anthropic
                ? currentRuntime.RequestedModel
                : ProviderRuntimeResolver.DefaultAnthropicModel,
            "gemini" => ProviderRuntimeResolver.DefaultGeminiModel,
            "github" => "github:copilot",
            "bedrock" or "vertex" or "foundry" => ProviderRuntimeResolver.DefaultAnthropicModel,
            "codex" => ProviderRuntimeResolver.DefaultCodexModel,
            "ollama" => "llama3.2",
            _ => ProviderRuntimeResolver.DefaultOpenAiModel
        };

        var model = (await interactionService.AskAsync($"Model [{defaultModel}]:", cancellationToken: cancellationToken)).Trim();
        if (string.IsNullOrWhiteSpace(model))
        {
            model = defaultModel;
        }

        return provider switch
        {
            "anthropic" => await ConfigureAnthropicAsync(interactionService, model, cancellationToken),
            "gemini" => await ConfigureGeminiAsync(interactionService, model, cancellationToken),
            "github" => await ConfigureGitHubAsync(interactionService, model, cancellationToken),
            "bedrock" or "vertex" or "foundry" => await ConfigureAnthropicCompatibleAsync(interactionService, provider, model, cancellationToken),
            "codex" => await ConfigureCodexAsync(interactionService, model, cancellationToken),
            "ollama" => await ConfigureOllamaAsync(interactionService, model, cancellationToken),
            _ => await ConfigureOpenAiAsync(interactionService, provider, model, cancellationToken)
        };
    }

    private static async Task<ProviderSelection> ConfigureAnthropicAsync(
        IInteractionService interactionService,
        string model,
        CancellationToken cancellationToken)
    {
        return await ConfigureAnthropicCompatibleAsync(
            interactionService,
            "anthropic",
            model,
            cancellationToken);
    }

    private static async Task<ProviderSelection> ConfigureAnthropicCompatibleAsync(
        IInteractionService interactionService,
        string provider,
        string model,
        CancellationToken cancellationToken)
    {
        var providerLabel = char.ToUpperInvariant(provider[0]) + provider[1..];
        var defaultBaseUrl = string.Equals(provider, "anthropic", StringComparison.Ordinal)
            ? ProviderRuntimeResolver.DefaultAnthropicBaseUrl
            : string.Empty;
        var prompt = string.IsNullOrWhiteSpace(defaultBaseUrl)
            ? $"{providerLabel} endpoint (leave blank to keep default/runtime endpoint):"
            : $"{providerLabel} endpoint [{defaultBaseUrl}] (leave blank for default):";
        var baseUrl = (await interactionService.AskAsync(
            prompt,
            cancellationToken: cancellationToken)).Trim();
        var secret = (await interactionService.AskAsync(
            $"{providerLabel} credential or API key (leave blank to keep existing auth):",
            secret: true,
            cancellationToken: cancellationToken)).Trim();

        return new ProviderSelection(
            provider,
            model,
            string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl,
            string.IsNullOrWhiteSpace(secret) ? null : secret,
            null,
            null);
    }

    private static async Task<ProviderSelection> ConfigureOpenAiAsync(
        IInteractionService interactionService,
        string provider,
        string model,
        CancellationToken cancellationToken)
    {
        var baseUrl = (await interactionService.AskAsync(
            $"Endpoint [{ProviderRuntimeResolver.DefaultOpenAiBaseUrl}]:",
            cancellationToken: cancellationToken)).Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = ProviderRuntimeResolver.DefaultOpenAiBaseUrl;
        }

        var apiKey = (await interactionService.AskAsync(
            "API key:",
            secret: true,
            cancellationToken: cancellationToken)).Trim();

        return new ProviderSelection(
            provider,
            model,
            baseUrl,
            string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
            null,
            null);
    }

    private static async Task<ProviderSelection> ConfigureGeminiAsync(
        IInteractionService interactionService,
        string model,
        CancellationToken cancellationToken)
    {
        var baseUrl = (await interactionService.AskAsync(
            $"Gemini endpoint [{ProviderRuntimeResolver.DefaultGeminiBaseUrl}]:",
            cancellationToken: cancellationToken)).Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = ProviderRuntimeResolver.DefaultGeminiBaseUrl;
        }

        var secretKind = await interactionService.SelectAsync(
            "Gemini credential type:",
            ["api-key", "access-token"],
            cancellationToken);
        var credential = (await interactionService.AskAsync(
            secretKind == "access-token" ? "Gemini access token:" : "Gemini API key:",
            secret: true,
            cancellationToken: cancellationToken)).Trim();

        return new ProviderSelection(
            "gemini",
            model,
            baseUrl,
            secretKind == "access-token" ? null : credential,
            secretKind == "access-token" ? credential : null,
            null);
    }

    private static async Task<ProviderSelection> ConfigureGitHubAsync(
        IInteractionService interactionService,
        string model,
        CancellationToken cancellationToken)
    {
        var token = (await interactionService.AskAsync(
            "GitHub token:",
            secret: true,
            cancellationToken: cancellationToken)).Trim();

        return new ProviderSelection(
            "github",
            model,
            ProviderRuntimeResolver.DefaultGitHubModelsBaseUrl,
            null,
            string.IsNullOrWhiteSpace(token) ? null : token,
            null);
    }

    private static async Task<ProviderSelection> ConfigureCodexAsync(
        IInteractionService interactionService,
        string model,
        CancellationToken cancellationToken)
    {
        var token = (await interactionService.AskAsync(
            "Codex access token:",
            secret: true,
            cancellationToken: cancellationToken)).Trim();
        var accountId = (await interactionService.AskAsync(
            "Codex account ID (optional):",
            cancellationToken: cancellationToken)).Trim();

        return new ProviderSelection(
            "codex",
            model,
            ProviderRuntimeResolver.DefaultCodexBaseUrl,
            string.IsNullOrWhiteSpace(token) ? null : token,
            null,
            string.IsNullOrWhiteSpace(accountId) ? null : accountId);
    }

    private static async Task<ProviderSelection> ConfigureOllamaAsync(
        IInteractionService interactionService,
        string model,
        CancellationToken cancellationToken)
    {
        var baseUrl = (await interactionService.AskAsync(
            "Ollama endpoint [http://localhost:11434/v1]:",
            cancellationToken: cancellationToken)).Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = "http://localhost:11434/v1";
        }

        return new ProviderSelection(
            "ollama",
            model,
            baseUrl,
            "ollama",
            null,
            null);
    }

    private static ProviderSelection? ParseArguments(
        IReadOnlyList<string> arguments,
        ProviderRuntimeConfig currentRuntime)
    {
        if (arguments.Count < 2)
        {
            return null;
        }

        var provider = arguments[1].Trim().ToLowerInvariant();
        if (!InteractiveProviders.Any(option => string.Equals(option, provider, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var model = arguments.Count > 2 && !string.IsNullOrWhiteSpace(arguments[2])
            ? arguments[2].Trim()
            : provider switch
            {
                "anthropic" => currentRuntime.Provider == ApiProviderKind.Anthropic
                    ? currentRuntime.RequestedModel
                    : ProviderRuntimeResolver.DefaultAnthropicModel,
                "gemini" => ProviderRuntimeResolver.DefaultGeminiModel,
                "github" => "github:copilot",
                "bedrock" or "vertex" or "foundry" => ProviderRuntimeResolver.DefaultAnthropicModel,
                "codex" => ProviderRuntimeResolver.DefaultCodexModel,
                "ollama" => "llama3.2",
                _ => ProviderRuntimeResolver.DefaultOpenAiModel
            };
        var baseUrl = arguments.Count > 3 && !string.IsNullOrWhiteSpace(arguments[3])
            ? arguments[3].Trim()
            : provider switch
            {
                "anthropic" => null,
                "gemini" => ProviderRuntimeResolver.DefaultGeminiBaseUrl,
                "github" => ProviderRuntimeResolver.DefaultGitHubModelsBaseUrl,
                "bedrock" or "vertex" or "foundry" => null,
                "codex" => ProviderRuntimeResolver.DefaultCodexBaseUrl,
                "ollama" => "http://localhost:11434/v1",
                _ => ProviderRuntimeResolver.DefaultOpenAiBaseUrl
            };
        var secret = arguments.Count > 4 && !string.IsNullOrWhiteSpace(arguments[4])
            ? arguments[4].Trim()
            : null;
        var accountId = arguments.Count > 5 && !string.IsNullOrWhiteSpace(arguments[5])
            ? arguments[5].Trim()
            : null;

        return provider switch
        {
            "anthropic" => new ProviderSelection(provider, model, baseUrl, secret, null, null),
            "github" => new ProviderSelection(provider, model, baseUrl, null, secret, null),
            "codex" => new ProviderSelection(provider, model, baseUrl, secret, null, accountId),
            "ollama" => new ProviderSelection(provider, model, baseUrl, secret ?? "ollama", null, null),
            _ => new ProviderSelection(provider, model, baseUrl, secret, null, null)
        };
    }

    private static ClawSharpSettings ApplySelection(ClawSharpSettings current, ProviderSelection selection)
    {
        var runtime = new RuntimeSettings
        {
            PermissionMode = current.Runtime.PermissionMode,
            Model = selection.Model,
            FallbackModel = current.Runtime.FallbackModel,
            EnableTelemetry = current.Runtime.EnableTelemetry,
            FileCheckpointingEnabled = current.Runtime.FileCheckpointingEnabled,
            AutoMemoryEnabled = current.Runtime.AutoMemoryEnabled,
            AutoMemoryDirectory = current.Runtime.AutoMemoryDirectory
        };
        var agentModels = new Dictionary<string, AgentModelConnection>(current.AgentModels, StringComparer.Ordinal);
        var agentRouting = new Dictionary<string, string>(current.AgentRouting, StringComparer.Ordinal);
        var claudeApiKey = current.ClaudeApiKey;

        if (string.Equals(selection.Provider, "anthropic", StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(selection.Secret))
            {
                claudeApiKey = selection.Secret;
            }

            if (!string.IsNullOrWhiteSpace(selection.BaseUrl) ||
                !string.IsNullOrWhiteSpace(selection.Secret))
            {
                agentModels[selection.Model] = new AgentModelConnection
                {
                    Provider = "anthropic",
                    BaseUrl = selection.BaseUrl ?? ProviderRuntimeResolver.DefaultAnthropicBaseUrl,
                    ApiKey = selection.Secret
                };
            }
            else
            {
                agentModels.Remove(selection.Model);
            }

            agentRouting.Remove("default");
        }
        else
        {
            agentModels[selection.Model] = new AgentModelConnection
            {
                Provider = selection.Provider switch
                {
                    "ollama" => "openai",
                    _ => selection.Provider
                },
                BaseUrl = selection.BaseUrl ?? ResolveDefaultBaseUrl(selection.Provider),
                ApiKey = selection.Secret,
                AuthToken = selection.AuthToken,
                AccountId = selection.AccountId
            };
            agentRouting["default"] = selection.Model;
        }

        return CloneSettings(
            current,
            runtime,
            claudeApiKey,
            agentModels,
            agentRouting);
    }

    private static void ApplyProviderEnvironment(ProviderSelection selection)
    {
        var args = new List<string> { "--provider", selection.Provider, "--model", selection.Model };
        ProviderFlagUtilities.ApplyProviderFlags(args);
    }

    private static string BuildStatusText(ProviderRuntimeConfig runtime)
    {
        return string.Join(
            Environment.NewLine,
            [
                $"Provider: {runtime.Provider}",
                $"Model: {runtime.RequestedModel}",
                $"Endpoint: {runtime.BaseUrl}",
                string.Empty,
                "Run /provider to configure the active provider.",
                "Usage: /provider [anthropic|openai|gemini|github|bedrock|vertex|foundry|codex|ollama] [model] [base-url] [secret] [account-id]"
            ]);
    }

    private static string BuildUsageText(ProviderRuntimeConfig runtime)
    {
        return BuildStatusText(runtime);
    }

    private static string BuildUpdatedText(ProviderSelection selection, ClawSharpSettings settings)
    {
        var runtime = ProviderRuntimeResolver.Resolve(settings, selection.Model);
        return string.Join(
            Environment.NewLine,
            [
                $"Provider updated to {runtime.Provider}.",
                $"Model: {runtime.RequestedModel}",
                $"Endpoint: {runtime.BaseUrl}"
            ]);
    }

    private static List<string> SplitArguments(string input)
    {
        return input
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
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

    private readonly record struct ProviderSelection(
        string Provider,
        string Model,
        string? BaseUrl,
        string? Secret,
        string? AuthToken,
        string? AccountId);
}
