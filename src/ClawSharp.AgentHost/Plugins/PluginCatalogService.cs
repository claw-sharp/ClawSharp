using System.Text.Json;
using ClawSharp.AgentHost.Contracts;
using ClawSharp.AgentHost.Ipc;
using ClawSharp.AgentHost.Projects;
using ClawSharp.AgentHost.Services;
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Tools.Mcp;

namespace ClawSharp.AgentHost.Plugins;

public sealed class PluginCatalogService
{
    private readonly WorkspaceApplicationRegistry _applicationRegistry;
    private readonly RecentProjectStore _recentProjectStore;
    private readonly IMcpSecureStorage _secureStorage;

    public PluginCatalogService(
        WorkspaceApplicationRegistry applicationRegistry,
        RecentProjectStore recentProjectStore,
        IMcpSecureStorage? secureStorage = null)
    {
        _applicationRegistry = applicationRegistry;
        _recentProjectStore = recentProjectStore;
        _secureStorage = secureStorage ?? McpSecureStorageFactory.CreateDefault();
    }

    public async Task<ListPluginsResponse> ListPluginsAsync(
        ListPluginsRequest request,
        CancellationToken cancellationToken = default)
    {
        var (projectId, app) = await ResolveApplicationAsync(request.ProjectId, cancellationToken);
        return BuildCatalog(projectId, app);
    }

    public async Task<ListPluginsResponse> SetPluginEnabledAsync(
        SetPluginEnabledRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PluginId))
        {
            throw new AgentHostException("invalid_request", "pluginId is required.");
        }

        var (projectId, app) = await ResolveApplicationAsync(request.ProjectId, cancellationToken);
        var state = app.AppStateStore.GetState();
        EnsurePluginExists(state, request.PluginId);

        var enabledPlugins = new Dictionary<string, PluginEnabledSetting>(state.Settings.EnabledPlugins, StringComparer.Ordinal);
        enabledPlugins[request.PluginId] = new PluginEnabledSetting
        {
            Enabled = request.Enabled,
            VersionConstraints = enabledPlugins.TryGetValue(request.PluginId, out var existing)
                ? existing.VersionConstraints
                : null
        };

        var updatedSettings = CloneSettings(state.Settings, enabledPlugins, state.Settings.PluginConfigs);
        await SaveAppSettingsAsync(app, updatedSettings, cancellationToken);
        await RefreshPluginsAsync(app, cancellationToken);
        await SyncRuntimeMcpAsync(app, cancellationToken);
        return BuildCatalog(projectId, app);
    }

    public async Task<InstallPluginResponse> InstallPluginAsync(
        InstallPluginRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PluginId))
        {
            throw new AgentHostException("invalid_request", "pluginId is required.");
        }

        var (projectId, app) = await ResolveApplicationAsync(request.ProjectId, cancellationToken);
        var state = app.AppStateStore.GetState();
        EnsurePluginExists(state, request.PluginId);

        if (!state.Settings.EnabledPlugins.TryGetValue(request.PluginId, out var existingEnabledSetting) || existingEnabledSetting.Enabled != true)
        {
            var enabledPlugins = new Dictionary<string, PluginEnabledSetting>(state.Settings.EnabledPlugins, StringComparer.Ordinal)
            {
                [request.PluginId] = new PluginEnabledSetting
                {
                    Enabled = true,
                    VersionConstraints = existingEnabledSetting?.VersionConstraints
                }
            };

            var updatedSettings = CloneSettings(state.Settings, enabledPlugins, state.Settings.PluginConfigs);
            await SaveAppSettingsAsync(app, updatedSettings, cancellationToken);
            await RefreshPluginsAsync(app, cancellationToken);
        }

        state = app.AppStateStore.GetState();
        var plugin = ResolvePlugin(state, request.PluginId);
        var pluginMcpResolver = new PluginMcpServerResolver(_secureStorage);
        var (pluginMcpServers, pluginMcpErrors) = pluginMcpResolver.Resolve([plugin], state.Settings);

        foreach (var error in pluginMcpErrors)
        {
            ClawSharpTelemetry.LogDebug(
                $"[PluginCatalogService:install] scope={error.Metadata.Scope} path={error.Path} message={error.Message}",
                error.Metadata.Severity == McpConfigErrorSeverity.Fatal ? DebugLogLevel.Warn : DebugLogLevel.Info);
        }

        if (pluginMcpServers.Count == 0)
        {
            if (pluginMcpErrors.Count > 0)
            {
                return new InstallPluginResponse(
                    projectId,
                    request.PluginId,
                    Enabled: true,
                    Authenticated: false,
                    Message: pluginMcpErrors[0].Message);
            }

            return new InstallPluginResponse(
                projectId,
                request.PluginId,
                Enabled: true,
                Authenticated: true,
                Message: $"Installed {plugin.Name}.");
        }

        var connections = new List<McpServerConnection>();
        foreach (var server in pluginMcpServers)
        {
            var connection = await app.McpLifecycleManager.ReconnectToServerWithoutInteractiveAuthAsync(
                server.Key,
                server.Value,
                cancellationToken: cancellationToken);
            connections.Add(connection);
        }

        if (app.IsRuntimeInitialized)
        {
            var runtime = await app.EnsureRuntimeAsync(cancellationToken);
            await app.McpToolRegistrationService.RegisterToolsAsync(runtime.Tools, connections, cancellationToken);
            await app.McpCommandResourceRegistrationService.RegisterForConnectionsAsync(runtime.Tools, connections, cancellationToken);
        }

        var connectedNames = connections
            .OfType<ConnectedMcpServerConnection>()
            .Select(static connection => connection.Name)
            .ToArray();
        if (connectedNames.Length > 0)
        {
            return new InstallPluginResponse(
                projectId,
                request.PluginId,
                Enabled: true,
                Authenticated: true,
                Message: $"Installed {plugin.Name} and authenticated {string.Join(", ", connectedNames)}.");
        }

        var failed = connections.OfType<FailedMcpServerConnection>().FirstOrDefault();
        if (failed is not null)
        {
            return new InstallPluginResponse(
                projectId,
                request.PluginId,
                Enabled: true,
                Authenticated: false,
                Message: string.IsNullOrWhiteSpace(failed.Error)
                    ? $"Installed {plugin.Name}, but authentication failed."
                    : failed.Error!);
        }

        return new InstallPluginResponse(
            projectId,
            request.PluginId,
            Enabled: true,
            Authenticated: false,
            Message: $"Installed {plugin.Name}. Authenticate when access is needed.");
    }

    public async Task<ListPluginsResponse> SavePluginOptionsAsync(
        SavePluginOptionsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PluginId))
        {
            throw new AgentHostException("invalid_request", "pluginId is required.");
        }

        var (projectId, app) = await ResolveApplicationAsync(request.ProjectId, cancellationToken);
        var state = app.AppStateStore.GetState();
        var plugin = ResolvePlugin(state, request.PluginId);
        if (plugin.UserConfig is null || plugin.UserConfig.Count == 0)
        {
            throw new AgentHostException("invalid_request", $"Plugin '{request.PluginId}' does not define configurable options.");
        }

        var parsedValues = ParseRequestedOptionValues(request.Values, plugin.UserConfig);
        var optionService = new PluginOptionService(_secureStorage);
        var existingValues = optionService.LoadPluginOptions(request.PluginId, state.Settings);
        var mergedPreview = MergeOptions(existingValues, parsedValues);
        ValidateRequiredOptions(plugin.UserConfig, mergedPreview);

        optionService.SavePluginOptions(
            request.PluginId,
            parsedValues,
            plugin.UserConfig,
            state.Settings,
            out var updatedSettings);

        await SaveAppSettingsAsync(app, updatedSettings, cancellationToken);
        await SyncRuntimeMcpAsync(app, cancellationToken);
        return BuildCatalog(projectId, app);
    }

    public async Task<ListPluginsResponse> DeletePluginOptionsAsync(
        DeletePluginOptionsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.PluginId))
        {
            throw new AgentHostException("invalid_request", "pluginId is required.");
        }

        var (projectId, app) = await ResolveApplicationAsync(request.ProjectId, cancellationToken);
        var state = app.AppStateStore.GetState();
        EnsurePluginExists(state, request.PluginId);

        var optionService = new PluginOptionService(_secureStorage);
        var updatedSettings = optionService.DeletePluginOptions(request.PluginId, state.Settings);
        await SaveAppSettingsAsync(app, updatedSettings, cancellationToken);
        await SyncRuntimeMcpAsync(app, cancellationToken);
        return BuildCatalog(projectId, app);
    }

    public async Task<ListPluginsResponse> RefreshPluginsAsync(
        ListPluginsRequest request,
        CancellationToken cancellationToken = default)
    {
        var (projectId, app) = await ResolveApplicationAsync(request.ProjectId, cancellationToken);
        await RefreshPluginsAsync(app, cancellationToken);
        await SyncRuntimeMcpAsync(app, cancellationToken);
        return BuildCatalog(projectId, app);
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

    private static void EnsurePluginExists(ClawSharpAppState state, string pluginId)
    {
        if (!state.Plugins.Any(plugin => string.Equals(plugin.PluginId, pluginId, StringComparison.Ordinal)))
        {
            throw new AgentHostException("plugin_not_found", $"Plugin '{pluginId}' was not found for the selected project.");
        }
    }

    private static DiscoveredPlugin ResolvePlugin(ClawSharpAppState state, string pluginId)
    {
        return state.Plugins.FirstOrDefault(plugin => string.Equals(plugin.PluginId, pluginId, StringComparison.Ordinal))
            ?? throw new AgentHostException("plugin_not_found", $"Plugin '{pluginId}' was not found for the selected project.");
    }

    private ListPluginsResponse BuildCatalog(string projectId, ClawSharpApplication app)
    {
        var state = app.AppStateStore.GetState();
        var optionService = new PluginOptionService(_secureStorage);
        var installationLookup = state.PluginInstallations
            .GroupBy(static installation => installation.PluginId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        var plugins = state.Plugins
            .OrderByDescending(static plugin => plugin.Enabled)
            .ThenBy(static plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
            .Select(plugin =>
            {
                installationLookup.TryGetValue(plugin.PluginId, out var installation);
                var authenticated = IsPluginAuthenticated(plugin, state.Settings, app.McpAuthStateService, _secureStorage);
                return new PluginSummaryDto(
                    plugin.PluginId,
                    plugin.Name,
                    plugin.Manifest?.Description,
                    installation?.Version ?? plugin.Manifest?.Version,
                    plugin.Enabled,
                    authenticated,
                    plugin.IsBundled,
                    plugin.InstallPath,
                    ResolvePluginScope(plugin, installation),
                    installation?.InstalledAt,
                    installation?.LastUpdated,
                    installation?.GitCommitSha,
                    plugin.Manifest?.Commands ?? [],
                    plugin.Manifest?.Agents ?? [],
                    plugin.Manifest?.Skills ?? [],
                    plugin.Manifest?.OutputStyles ?? [],
                    plugin.Manifest?.HookFiles ?? [],
                    plugin.Hooks.Keys.Select(static hookEvent => hookEvent.ToString()).ToArray(),
                    plugin.ValidationIssues
                        .Select(static issue => new PluginValidationIssueDto(issue.Path, issue.Message, issue.IsWarning))
                        .ToArray(),
                    MapPluginOptions(plugin, state.Settings, optionService),
                    MapPluginMcpServers(plugin));
            })
            .ToArray();

        return new ListPluginsResponse(projectId, state.WorkspaceRoot, plugins);
    }

    private static bool IsPluginAuthenticated(
        DiscoveredPlugin plugin,
        ClawSharpSettings settings,
        McpAuthStateService authStateService,
        IMcpSecureStorage secureStorage)
    {
        if (plugin.Manifest?.McpServers.Count is not > 0)
        {
            return false;
        }

        var resolver = new PluginMcpServerResolver(secureStorage);
        var (resolvedServers, _) = resolver.Resolve([plugin], settings);
        if (resolvedServers.Count == 0)
        {
            return false;
        }

        var authCapableServers = resolvedServers
            .Where(static entry => entry.Value.Config is McpHttpServerConfig or McpSseServerConfig)
            .Where(entry =>
            {
                var oauthEntry = authStateService.GetOAuthEntry(entry.Key, entry.Value.Config);
                return oauthEntry is not null ||
                       authStateService.HasDiscoveryButNoToken(entry.Key, entry.Value.Config) ||
                       entry.Value.Config switch
                       {
                           McpHttpServerConfig http => http.OAuth is not null,
                           McpSseServerConfig sse => sse.OAuth is not null,
                           _ => false
                       };
            })
            .ToArray();

        return authCapableServers.Length > 0 &&
               authCapableServers.All(entry =>
               {
                   var oauthEntry = authStateService.GetOAuthEntry(entry.Key, entry.Value.Config);
                   return oauthEntry is not null &&
                          (!string.IsNullOrWhiteSpace(oauthEntry.AccessToken) ||
                           !string.IsNullOrWhiteSpace(oauthEntry.RefreshToken));
               });
    }

    private static IReadOnlyList<PluginOptionDto> MapPluginOptions(
        DiscoveredPlugin plugin,
        ClawSharpSettings settings,
        PluginOptionService optionService)
    {
        if (plugin.UserConfig is null || plugin.UserConfig.Count == 0)
        {
            return [];
        }

        var resolvedValues = optionService.LoadPluginOptions(plugin.PluginId, settings);
        return plugin.UserConfig
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair =>
            {
                var definition = pair.Value;
                resolvedValues.TryGetValue(pair.Key, out var value);
                return new PluginOptionDto(
                    pair.Key,
                    definition.Type.ToString().ToLowerInvariant(),
                    definition.Title,
                    definition.Description,
                    definition.Required,
                    definition.Multiple,
                    definition.Sensitive,
                    HasConfiguredValue(value),
                    definition.Sensitive ? null : value,
                    definition.DefaultValue,
                    definition.Min,
                    definition.Max);
            })
            .ToArray();
    }

    private static IReadOnlyList<PluginMcpServerDto> MapPluginMcpServers(DiscoveredPlugin plugin)
    {
        return plugin.Manifest?.McpServers
            .Select(static server => new PluginMcpServerDto(
                server.Name,
                server.Config.Type,
                server.Config switch
                {
                    McpHttpServerConfig http => http.Url,
                    McpSseServerConfig sse => sse.Url,
                    McpWebSocketServerConfig webSocket => webSocket.Url,
                    McpSseIdeServerConfig sseIde => sseIde.Url,
                    McpWebSocketIdeServerConfig webSocketIde => webSocketIde.Url,
                    McpSdkServerConfig sdk => sdk.Name,
                    McpClaudeAiProxyServerConfig proxy => proxy.Url,
                    McpStdioServerConfig stdio => stdio.Command,
                    _ => null
                }))
            .ToArray()
            ?? [];
    }

    private static IReadOnlyDictionary<string, object?> ParseRequestedOptionValues(
        IReadOnlyDictionary<string, JsonElement>? values,
        IReadOnlyDictionary<string, PluginOptionDefinition> schema)
    {
        var parsed = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (values is null)
        {
            return parsed;
        }

        foreach (var pair in values)
        {
            if (!schema.TryGetValue(pair.Key, out var definition))
            {
                throw new AgentHostException("invalid_request", $"Plugin option '{pair.Key}' is not defined by this plugin.");
            }

            parsed[pair.Key] = ParseOptionValue(pair.Key, pair.Value, definition);
        }

        return parsed;
    }

    private static object? ParseOptionValue(string key, JsonElement value, PluginOptionDefinition definition)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (definition.Multiple)
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                throw new AgentHostException("invalid_request", $"Plugin option '{key}' expects an array value.");
            }

            return value.EnumerateArray()
                .Select(item => ParseSingleOptionValue(key, item, definition))
                .ToArray();
        }

        return ParseSingleOptionValue(key, value, definition);
    }

    private static object? ParseSingleOptionValue(string key, JsonElement value, PluginOptionDefinition definition)
    {
        try
        {
            return definition.Type switch
            {
                PluginOptionType.String or PluginOptionType.Directory or PluginOptionType.File =>
                    value.ValueKind == JsonValueKind.String
                        ? value.GetString()
                        : throw new AgentHostException("invalid_request", $"Plugin option '{key}' expects a string value."),
                PluginOptionType.Number =>
                    value.ValueKind == JsonValueKind.Number
                        ? value.GetDouble()
                        : throw new AgentHostException("invalid_request", $"Plugin option '{key}' expects a numeric value."),
                PluginOptionType.Boolean =>
                    value.ValueKind is JsonValueKind.True or JsonValueKind.False
                        ? value.GetBoolean()
                        : throw new AgentHostException("invalid_request", $"Plugin option '{key}' expects a boolean value."),
                _ => throw new AgentHostException("invalid_request", $"Plugin option '{key}' uses an unsupported type.")
            };
        }
        catch (InvalidOperationException)
        {
            throw new AgentHostException("invalid_request", $"Plugin option '{key}' could not be parsed.");
        }
    }

    private static IReadOnlyDictionary<string, object?> MergeOptions(
        IReadOnlyDictionary<string, object?> existingValues,
        IReadOnlyDictionary<string, object?> updates)
    {
        var merged = new Dictionary<string, object?>(existingValues, StringComparer.Ordinal);
        foreach (var pair in updates)
        {
            if (pair.Value is null)
            {
                merged.Remove(pair.Key);
            }
            else
            {
                merged[pair.Key] = pair.Value;
            }
        }

        return merged;
    }

    private static void ValidateRequiredOptions(
        IReadOnlyDictionary<string, PluginOptionDefinition> schema,
        IReadOnlyDictionary<string, object?> mergedValues)
    {
        foreach (var pair in schema)
        {
            if (!pair.Value.Required)
            {
                continue;
            }

            mergedValues.TryGetValue(pair.Key, out var value);
            if (!HasConfiguredValue(value))
            {
                throw new AgentHostException("invalid_request", $"Plugin option '{pair.Key}' is required.");
            }
        }
    }

    private static bool HasConfiguredValue(object? value)
    {
        return value switch
        {
            null => false,
            string text => !string.IsNullOrWhiteSpace(text),
            Array array => array.Length > 0,
            _ => true
        };
    }

    private static string ResolvePluginScope(
        DiscoveredPlugin plugin,
        DiscoveredPluginInstallation? installation)
    {
        if (installation is not null)
        {
            return installation.Scope.ToString().ToLowerInvariant();
        }

        if (plugin.PluginId.EndsWith("@builtin", StringComparison.OrdinalIgnoreCase) || plugin.IsBundled)
        {
            return "builtin";
        }

        return "unknown";
    }

    private static async Task SaveAppSettingsAsync(
        ClawSharpApplication app,
        ClawSharpSettings updatedSettings,
        CancellationToken cancellationToken)
    {
        app.AppStateStore.SetState(state => ClawSharpAppStateMutations.WithSettings(state, updatedSettings));
        await app.SettingsStore.SaveAsync(updatedSettings, cancellationToken);
    }

    private static async Task RefreshPluginsAsync(
        ClawSharpApplication app,
        CancellationToken cancellationToken)
    {
        var refreshService = new PluginRefreshService(
            new ExtensionBootstrapper(builtInPluginRegistry: BuiltInPluginCatalog.CreateRegistry()),
            app.AppStateStore);
        await refreshService.RefreshAsync(cancellationToken);
    }

    private async Task SyncRuntimeMcpAsync(
        ClawSharpApplication app,
        CancellationToken cancellationToken)
    {
        if (!app.IsRuntimeInitialized)
        {
            return;
        }

        var runtime = await app.EnsureRuntimeAsync(cancellationToken);
        runtime.Tools.UnregisterWhere(
            name => name.StartsWith("mcp__", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, ListMcpResourcesTool.ToolName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, ReadMcpResourceTool.ToolName, StringComparison.OrdinalIgnoreCase));
        app.McpPromptCommands.UnregisterWhere(name => name.StartsWith("mcp__", StringComparison.OrdinalIgnoreCase));
        runtime.Tools.McpResources.Clear();
        app.McpResourceCatalog.Clear();

        var state = app.AppStateStore.GetState();
        var pluginMcpResolver = new PluginMcpServerResolver(_secureStorage);
        var (pluginMcpServers, pluginMcpErrors) = pluginMcpResolver.Resolve(state.Plugins, state.Settings);
        var (configuredMcpServers, mcpConfigErrors) = app.McpConfigService.GetAllConfigs(pluginMcpServers);

        foreach (var error in pluginMcpErrors.Concat(mcpConfigErrors))
        {
            ClawSharpTelemetry.LogDebug(
                $"[PluginCatalogService:mcp-sync] scope={error.Metadata.Scope} path={error.Path} message={error.Message}",
                error.Metadata.Severity == McpConfigErrorSeverity.Fatal ? DebugLogLevel.Warn : DebugLogLevel.Info);
        }

        if (configuredMcpServers.Count == 0)
        {
            return;
        }

        var connections = await app.McpLifecycleManager.ConnectServersWithoutInteractiveAuthAsync(
            configuredMcpServers,
            cancellationToken: cancellationToken);
        await app.McpToolRegistrationService.RegisterToolsAsync(runtime.Tools, connections, cancellationToken);
        await app.McpCommandResourceRegistrationService.RegisterForConnectionsAsync(runtime.Tools, connections, cancellationToken);
    }

    private static ClawSharpSettings CloneSettings(
        ClawSharpSettings source,
        IReadOnlyDictionary<string, PluginEnabledSetting> enabledPlugins,
        IReadOnlyDictionary<string, PluginConfigSettings> pluginConfigs)
    {
        return new ClawSharpSettings
        {
            Runtime = source.Runtime,
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
            EnabledPlugins = enabledPlugins,
            PluginConfigs = pluginConfigs,
            AgentModels = source.AgentModels,
            AgentRouting = source.AgentRouting
        };
    }
}
