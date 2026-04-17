using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClawSharp.AgentHost.Contracts;
using ClawSharp.AgentHost.Ipc;
using ClawSharp.AgentHost.Projects;
using ClawSharp.AgentHost.Services;
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Query;

namespace ClawSharp.AgentHost.Agents;

public sealed class AgentCatalogService
{
    private const string AgentCreationQuerySource = "agent_creation";
    private static readonly Regex AgentTypePattern = new("^[A-Za-z0-9][A-Za-z0-9-]*[A-Za-z0-9]$", RegexOptions.Compiled);
    private static readonly Regex JsonObjectPattern = new("\\{[\\s\\S]*\\}", RegexOptions.Compiled);
    private static readonly TimeSpan ProposalTimeout = TimeSpan.FromSeconds(60);
    private const string AgentCreationSystemPrompt = """
You are an expert agent architect. Turn the user's request into a precise reusable sub-agent definition.

Your output must be a valid JSON object with exactly these fields:
{
  "identifier": "lowercase letters, numbers, and hyphens only",
  "whenToUse": "A precise description starting with 'Use this agent when...'",
  "systemPrompt": "The full system prompt for the agent"
}

Requirements:
- The identifier must be concise, descriptive, and easy to type.
- Do not use an identifier that already exists if the prompt lists existing identifiers.
- The whenToUse field must clearly describe trigger conditions and include short examples when helpful.
- The systemPrompt must be written as direct instructions to the child agent.
- Prefer concrete workflows, output expectations, and quality checks over generic advice.
- If the user implies persistent memory, mention concise memory update guidance in the systemPrompt.
- Return JSON only. Do not wrap it in markdown fences.
""";

    private readonly WorkspaceApplicationRegistry _applicationRegistry;
    private readonly RecentProjectStore _recentProjectStore;
    private readonly IMcpSecureStorage _secureStorage;
    private readonly QueryRequestBuilder _queryRequestBuilder = new();

    public AgentCatalogService(
        WorkspaceApplicationRegistry applicationRegistry,
        RecentProjectStore recentProjectStore,
        IMcpSecureStorage? secureStorage = null)
    {
        _applicationRegistry = applicationRegistry;
        _recentProjectStore = recentProjectStore;
        _secureStorage = secureStorage ?? McpSecureStorageFactory.CreateDefault();
    }

    public async Task<ListAgentsResponse> ListAgentsAsync(
        ListAgentsRequest request,
        CancellationToken cancellationToken = default)
    {
        var (projectId, app) = await ResolveApplicationAsync(request.ProjectId, cancellationToken);
        return BuildCatalog(projectId, app);
    }

    public async Task<CreateAgentResponse> CreateAgentAsync(
        CreateAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        var identifier = request.Identifier?.Trim();
        ValidateIdentifier(identifier);

        var whenToUse = request.WhenToUse?.Trim();
        if (string.IsNullOrWhiteSpace(whenToUse))
        {
            throw new AgentHostException("invalid_request", "Agent usage guidance is required.");
        }

        var systemPrompt = request.SystemPrompt?.Trim();
        if (string.IsNullOrWhiteSpace(systemPrompt))
        {
            throw new AgentHostException("invalid_request", "Agent system prompt is required.");
        }

        var (projectId, app) = await ResolveApplicationAsync(request.ProjectId, cancellationToken);
        var state = app.AppStateStore.GetState();
        EnsureAgentDoesNotAlreadyExist(state.AgentDefinitions, identifier!);

        var agentDirectory = Path.Combine(state.WorkspaceRoot, ".clawsharp", "agents");
        var agentFilePath = Path.Combine(agentDirectory, $"{identifier}.md");
        if (File.Exists(agentFilePath))
        {
            throw new AgentHostException("agent_exists", $"Agent path already exists: {agentFilePath}");
        }

        Directory.CreateDirectory(agentDirectory);
        var normalizedRequest = new CreateAgentRequest
        {
            ProjectId = request.ProjectId,
            Identifier = identifier,
            WhenToUse = whenToUse,
            SystemPrompt = systemPrompt,
            Model = request.Model,
            Color = request.Color,
            Tools = request.Tools,
            DisallowedTools = request.DisallowedTools,
            Skills = request.Skills,
            PermissionMode = request.PermissionMode,
            MaxTurns = request.MaxTurns,
            Background = request.Background,
            InitialPrompt = request.InitialPrompt,
            Memory = request.Memory,
            Isolation = request.Isolation,
            OmitClaudeMd = request.OmitClaudeMd
        };
        await File.WriteAllTextAsync(
            agentFilePath,
            BuildAgentTemplate(normalizedRequest),
            Encoding.UTF8,
            cancellationToken);

        await RefreshAgentsAsync(app, cancellationToken);

        var response = BuildCatalog(projectId, app);
        var createdAgent = response.Agents.FirstOrDefault(agent =>
            string.Equals(agent.Identifier, identifier, StringComparison.OrdinalIgnoreCase));
        if (createdAgent is null)
        {
            throw new AgentHostException("agent_refresh_failed", $"Agent '{identifier}' was created but did not appear in the refreshed catalog.");
        }

        return new CreateAgentResponse(projectId, response.WorkspaceRoot, createdAgent, response.Agents);
    }

    public async Task<ProposeAgentResponse> ProposeAgentAsync(
        ProposeAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        var userPrompt = request.Prompt?.Trim();
        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            throw new AgentHostException("invalid_request", "A prompt is required to propose an agent.");
        }

        var (projectId, app) = await ResolveApplicationAsync(request.ProjectId, cancellationToken);
        var proposal = await GenerateProposalAsync(app, userPrompt, request.Model, cancellationToken);
        return new ProposeAgentResponse(projectId, app.AppStateStore.GetState().WorkspaceRoot, proposal);
    }

    private async Task<AgentProposalDto> GenerateProposalAsync(
        ClawSharpApplication app,
        string userPrompt,
        string? explicitModel,
        CancellationToken cancellationToken)
    {
        var runtime = await app.EnsureRuntimeAsync(cancellationToken);
        var mainThreadContext = runtime.ModelTurnContextProvider is not null
            ? await runtime.ModelTurnContextProvider.GetReplMainThreadContextAsync(cancellationToken)
            : QueryModelTurnContext.ReplMainThread;

        var state = app.AppStateStore.GetState();
        var resolvedModel = ResolveModel(state.Settings, explicitModel);
        var prompt = BuildProposalPrompt(userPrompt, state.AgentDefinitions);
        var session = new ConversationSession($"agent-proposal-{Guid.NewGuid():N}", state.WorkspaceRoot);
        var userMessage = ChatMessageFactory.CreateText(MessageRole.User, prompt);
        var request = QueryTurnRequest.Create(session, prompt) with
        {
            ModelTurnContext = new QueryModelTurnContext(
                mainThreadContext.SystemPrompt.Concat([AgentCreationSystemPrompt]).ToArray(),
                mainThreadContext.UserContext,
                mainThreadContext.SystemContext,
                AgentCreationQuerySource)
        };
        var settings = CloneSettingsForModel(state.Settings, resolvedModel);
        var modelRequest = _queryRequestBuilder.BuildFromMessages(
            request,
            [userMessage],
            settings,
            [],
            new QueryRequestBuildOptions(
                SystemPrompt: request.ModelTurnContext.SystemPrompt,
                SystemContext: request.ModelTurnContext.SystemContext,
                UserContext: request.ModelTurnContext.UserContext));
        var streamingRequest = new QueryModelHttpStreamingRequest(modelRequest, AgentCreationQuerySource);
        var loopState = new QueryLoopState(
            [],
            0,
            QueryToolUseContextState.Empty with { MainLoopModel = resolvedModel });
        var modelExecutor = CreateModelExecutor();
        var responseBuilder = new StringBuilder();
        QueryModelCallAttemptResult? attemptResult = null;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ProposalTimeout);

        await foreach (var update in modelExecutor.StreamAsync(
                           streamingRequest,
                           request,
                           loopState,
                           session,
                           settings,
                           timeoutCts.Token))
        {
            if (update.RuntimeEvent is QueryStreamDeltaRuntimeEvent deltaEvent)
            {
                responseBuilder.Append(deltaEvent.Delta);
            }

            if (update.AttemptResult is not null)
            {
                attemptResult = update.AttemptResult;
            }
        }

        var responseText = responseBuilder.ToString().Trim();
        if (string.IsNullOrWhiteSpace(responseText) &&
            attemptResult?.IterationResult is QueryTerminalIterationResult terminalResult)
        {
            responseText = ExtractAssistantText(terminalResult.State.Messages);
        }

        if (string.IsNullOrWhiteSpace(responseText))
        {
            throw new AgentHostException("proposal_failed", "The model did not return an agent proposal.");
        }

        var parsed = ParseProposal(responseText);
        ValidateIdentifier(parsed.Identifier);
        EnsureAgentDoesNotAlreadyExist(state.AgentDefinitions, parsed.Identifier);
        if (string.IsNullOrWhiteSpace(parsed.WhenToUse))
        {
            throw new AgentHostException("proposal_failed", "The generated proposal is missing usage guidance.");
        }

        if (string.IsNullOrWhiteSpace(parsed.SystemPrompt))
        {
            throw new AgentHostException("proposal_failed", "The generated proposal is missing a system prompt.");
        }

        return parsed with
        {
            Identifier = parsed.Identifier.Trim(),
            WhenToUse = parsed.WhenToUse.Trim(),
            SystemPrompt = parsed.SystemPrompt.Trim()
        };
    }

    private async Task<(string ProjectId, ClawSharpApplication App)> ResolveApplicationAsync(
        string? projectId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            var project = await _recentProjectStore.FindByIdAsync(projectId, cancellationToken);
            if (project is null)
            {
                throw new AgentHostException("project_not_found", $"Project '{projectId}' is not known to AgentHost yet.");
            }

            return (project.ProjectId, await _applicationRegistry.GetOrCreateAsync(project.Path, cancellationToken));
        }

        var recentProject = (await _recentProjectStore.ListAsync(cancellationToken)).FirstOrDefault();
        if (recentProject is null)
        {
            throw new AgentHostException("project_not_found", "No project is currently open.");
        }

        return (recentProject.ProjectId, await _applicationRegistry.GetOrCreateAsync(recentProject.Path, cancellationToken));
    }

    private static ListAgentsResponse BuildCatalog(string projectId, ClawSharpApplication app)
    {
        var state = app.AppStateStore.GetState();
        var agents = state.AgentDefinitions
            .OrderBy(static agent => agent.AgentType, StringComparer.OrdinalIgnoreCase)
            .Select(MapAgentSummary)
            .ToArray();

        return new ListAgentsResponse(projectId, state.WorkspaceRoot, agents);
    }

    private static AgentSummaryDto MapAgentSummary(AgentDefinition agent)
    {
        var filePath = string.IsNullOrWhiteSpace(agent.Filename)
            ? null
            : Path.Combine(agent.BaseDirectory, $"{agent.Filename}.md");
        return new AgentSummaryDto(
            agent.AgentType,
            agent.WhenToUse,
            agent.Source,
            agent.BaseDirectory,
            filePath,
            agent.SystemPrompt,
            agent.Tools,
            agent.DisallowedTools,
            agent.Skills,
            agent.Color,
            agent.Model,
            agent.PermissionMode?.ToString(),
            agent.MaxTurns,
            agent.Filename,
            agent.Background,
            agent.InitialPrompt,
            agent.Memory,
            agent.Isolation,
            agent.OmitClaudeMd);
    }

    private static void ValidateIdentifier(string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new AgentHostException("invalid_request", "Agent identifier is required.");
        }

        if (identifier.Length < 3)
        {
            throw new AgentHostException("invalid_request", "Agent identifier must be at least 3 characters long.");
        }

        if (identifier.Length > 50)
        {
            throw new AgentHostException("invalid_request", "Agent identifier must be 50 characters or fewer.");
        }

        if (!AgentTypePattern.IsMatch(identifier))
        {
            throw new AgentHostException(
                "invalid_request",
                "Agent identifier must start and end with a letter or number and may only contain letters, numbers, and hyphens.");
        }
    }

    private static void EnsureAgentDoesNotAlreadyExist(
        IReadOnlyList<AgentDefinition> existingAgents,
        string identifier)
    {
        if (existingAgents.Any(agent => string.Equals(agent.AgentType, identifier, StringComparison.OrdinalIgnoreCase)))
        {
            throw new AgentHostException("agent_exists", $"An agent named '{identifier}' is already available in this project.");
        }
    }

    private static string BuildProposalPrompt(
        string userPrompt,
        IReadOnlyList<AgentDefinition> existingAgents)
    {
        var existingIdentifiers = existingAgents
            .Select(static agent => agent.AgentType)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var existingText = existingIdentifiers.Length == 0
            ? "No existing identifiers are reserved."
            : $"Existing identifiers that must not be reused: {string.Join(", ", existingIdentifiers)}.";

        return $"""
Create an agent configuration for this request:
{userPrompt}

{existingText}

Return only the JSON object.
""";
    }

    private static AgentProposalDto ParseProposal(string responseText)
    {
        var candidateJson = responseText.Trim();
        try
        {
            return ParseProposalJson(candidateJson);
        }
        catch (JsonException)
        {
            var match = JsonObjectPattern.Match(candidateJson);
            if (!match.Success)
            {
                throw new AgentHostException("proposal_failed", "The generated proposal did not contain a valid JSON object.");
            }

            try
            {
                return ParseProposalJson(match.Value);
            }
            catch (JsonException exception)
            {
                throw new AgentHostException("proposal_failed", $"The generated proposal could not be parsed: {exception.Message}");
            }
        }
    }

    private static AgentProposalDto ParseProposalJson(string json)
    {
        var payload = JsonSerializer.Deserialize<GeneratedAgentPayload>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        if (payload is null ||
            string.IsNullOrWhiteSpace(payload.Identifier) ||
            string.IsNullOrWhiteSpace(payload.WhenToUse) ||
            string.IsNullOrWhiteSpace(payload.SystemPrompt))
        {
            throw new AgentHostException("proposal_failed", "The generated proposal omitted one or more required fields.");
        }

        return new AgentProposalDto(
            payload.Identifier,
            payload.WhenToUse,
            payload.SystemPrompt);
    }

    private static string ExtractAssistantText(IReadOnlyList<ChatMessage> messages)
    {
        for (var index = messages.Count - 1; index >= 0; index--)
        {
            var content = string.Join(
                "\n",
                messages[index].ContentBlocks
                    .Where(static block => block.Kind == MessageContentKind.Text)
                    .Select(static block => block.Value)
                    .Where(static value => !string.IsNullOrWhiteSpace(value)))
                .Trim();
            if (!string.IsNullOrWhiteSpace(content))
            {
                return content;
            }
        }

        return string.Empty;
    }

    private static IQueryModelCallExecutor CreateModelExecutor(IMcpSecureStorage? secureStorage = null)
    {
        var resolvedStorage = secureStorage ?? McpSecureStorageFactory.CreateDefault();
        return new QueryModelHttpCallExecutor(
            new EnvironmentQueryModelHttpClientConfigProvider(resolvedStorage),
            new QueryModelSseStreamingClient(),
            new QueryModelAnthropicStreamUpdateParser(),
            new SecureStorageQueryAuthAccountStateProvider(resolvedStorage),
            new NoOpQueryAuthFailureRecoveryRunner());
    }

    private async Task RefreshAgentsAsync(
        ClawSharpApplication app,
        CancellationToken cancellationToken)
    {
        var state = app.AppStateStore.GetState();
        var catalog = await new AgentBootstrapper().LoadAsync(
            state.WorkspaceRoot,
            state.Environment,
            cancellationToken);
        app.AppStateStore.SetState(current => current with
        {
            AgentDefinitions = catalog.ActiveAgents
        });
    }

    private static string ResolveModel(ClawSharpSettings settings, string? explicitModel)
    {
        var requestedModel = string.IsNullOrWhiteSpace(explicitModel)
            ? settings.Runtime.Model
            : explicitModel.Trim();
        return MainLoopModelResolver.Resolve(requestedModel, settings.Runtime.Model);
    }

    private static ClawSharpSettings CloneSettingsForModel(ClawSharpSettings source, string model)
    {
        return new ClawSharpSettings
        {
            Runtime = new RuntimeSettings
            {
                PermissionMode = source.Runtime.PermissionMode,
                Model = model,
                FallbackModel = source.Runtime.FallbackModel,
                EnableTelemetry = source.Runtime.EnableTelemetry,
                FileCheckpointingEnabled = source.Runtime.FileCheckpointingEnabled,
                AutoMemoryEnabled = source.Runtime.AutoMemoryEnabled,
                AutoMemoryDirectory = source.Runtime.AutoMemoryDirectory
            },
            Terminal = source.Terminal,
            Sandbox = source.Sandbox,
            ClaudeApiKey = source.ClaudeApiKey,
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
            AgentModels = source.AgentModels,
            AgentRouting = source.AgentRouting
        };
    }

    private static string BuildAgentTemplate(CreateAgentRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.Append("name: ").AppendLine(request.Identifier!);
        AppendYamlBlock(builder, "description", request.WhenToUse!);
        AppendYamlString(builder, "model", request.Model);
        AppendYamlString(builder, "color", request.Color);
        AppendYamlList(builder, "tools", request.Tools);
        AppendYamlList(builder, "disallowedTools", request.DisallowedTools);
        AppendYamlList(builder, "skills", request.Skills);
        AppendYamlString(builder, "permissionMode", request.PermissionMode);
        AppendYamlInt(builder, "maxTurns", request.MaxTurns);
        AppendYamlBool(builder, "background", request.Background);
        AppendYamlBlock(builder, "initialPrompt", request.InitialPrompt);
        AppendYamlString(builder, "memory", request.Memory);
        AppendYamlString(builder, "isolation", request.Isolation);
        AppendYamlBool(builder, "omitClaudeMd", request.OmitClaudeMd);
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine(request.SystemPrompt!.Trim());
        builder.AppendLine();
        return builder.ToString();
    }

    private static void AppendYamlString(StringBuilder builder, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        builder.Append(key)
            .Append(": \"")
            .Append(value.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal))
            .AppendLine("\"");
    }

    private static void AppendYamlBlock(StringBuilder builder, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        builder.Append(key).AppendLine(": |-");
        foreach (var line in value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            builder.Append("  ").AppendLine(line);
        }
    }

    private static void AppendYamlList(StringBuilder builder, string key, IReadOnlyList<string>? values)
    {
        var normalized = values?
            .Select(static value => value?.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized is not { Length: > 0 })
        {
            return;
        }

        builder.Append(key).AppendLine(":");
        foreach (var value in normalized)
        {
            builder.Append("  - \"")
                .Append(value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal))
                .AppendLine("\"");
        }
    }

    private static void AppendYamlInt(StringBuilder builder, string key, int? value)
    {
        if (value is not > 0)
        {
            return;
        }

        builder.Append(key).Append(": ").Append(value.Value).AppendLine();
    }

    private static void AppendYamlBool(StringBuilder builder, string key, bool? value)
    {
        if (!value.HasValue)
        {
            return;
        }

        builder.Append(key).Append(": ").Append(value.Value ? "true" : "false").AppendLine();
    }

    private sealed record GeneratedAgentPayload(
        string Identifier,
        string WhenToUse,
        string SystemPrompt);
}
