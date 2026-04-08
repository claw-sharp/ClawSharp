using System.Text;
using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Tools;
using ClawSharp.Tools.Mcp;

namespace ClawSharp.Infrastructure;

public sealed class McpCommandResourceRegistrationService
{
    private readonly McpLifecycleManager _lifecycleManager;
    private readonly McpPromptCommandRegistry _promptCommands;
    private readonly McpResourceCatalog _resourceCatalog;

    public McpCommandResourceRegistrationService(
        McpLifecycleManager lifecycleManager,
        McpPromptCommandRegistry promptCommands,
        McpResourceCatalog resourceCatalog)
    {
        _lifecycleManager = lifecycleManager;
        _promptCommands = promptCommands;
        _resourceCatalog = resourceCatalog;
    }

    public async Task<IReadOnlyList<IMcpPromptCommandHandler>> FetchCommandsForConnectionAsync(
        McpServerConnection connection,
        CancellationToken cancellationToken = default)
    {
        if (connection is not ConnectedMcpServerConnection connected ||
            !connected.Capabilities.ContainsKey("prompts"))
        {
            return [];
        }

        var prompts = await connected.Client.ListPromptsAsync(cancellationToken).ConfigureAwait(false);
        return prompts
            .Select(prompt => (IMcpPromptCommandHandler)new RegisteredMcpPromptCommandHandler(_lifecycleManager, connected.Name, connected.Config, prompt))
            .ToArray();
    }

    public async Task<IReadOnlyList<McpServerResource>> FetchResourcesForConnectionAsync(
        McpServerConnection connection,
        CancellationToken cancellationToken = default)
    {
        if (connection is not ConnectedMcpServerConnection connected ||
            !connected.Capabilities.ContainsKey("resources"))
        {
            return [];
        }

        var resources = await connected.Client.ListResourcesAsync(cancellationToken).ConfigureAwait(false);
        return resources
            .Select(
                resource => new McpServerResource(
                    connected.Name,
                    resource.Uri,
                    resource.Name,
                    resource.MimeType,
                    resource.Description))
            .ToArray();
    }

    public async Task RegisterForConnectionsAsync(
        ToolRegistry toolRegistry,
        IReadOnlyList<McpServerConnection> connections,
        CancellationToken cancellationToken = default)
    {
        var shouldRegisterResourceTools = false;

        foreach (var connection in connections)
        {
            var fetchedCommands = await FetchCommandsForConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"DEBUG: FetchCommandsForConnectionAsync returned {fetchedCommands.Count} commands for connection {connection.Name}");
            foreach (var command in fetchedCommands)
            {
                Console.WriteLine($"DEBUG: Registering prompt command: {command.Descriptor.Name}");
                _promptCommands.RegisterOrReplace(command);
            }

            if (connection is not ConnectedMcpServerConnection connected)
            {
                continue;
            }

            var resources = await FetchResourcesForConnectionAsync(connection, cancellationToken).ConfigureAwait(false);
            if (resources.Count > 0)
            {
                _resourceCatalog.RegisterOrReplace(connected.Name, connected.Config, resources);
                // Also register resources into the ToolRegistry's MCP catalog if the caller provided one
                try
                {
                    toolRegistry?.McpResources?.RegisterOrReplace(connected.Name, connected.Config, resources);
                }
                catch
                {
                    // Ignore any issues while synchronizing catalogs to avoid breaking registration flow
                }
            }

            shouldRegisterResourceTools |= connected.Capabilities.ContainsKey("resources");
        }

        if (shouldRegisterResourceTools)
        {
            // Ensure registered tools have access to the lifecycle manager and resource catalog when executed
            ListMcpResourcesTool.DefaultCatalog = toolRegistry.McpResources ?? _resourceCatalog;
            ReadMcpResourceTool.DefaultCatalog = toolRegistry.McpResources ?? _resourceCatalog;
            ReadMcpResourceTool.DefaultLifecycle = _lifecycleManager;

            toolRegistry.RegisterOrReplace(new ListMcpResourcesTool());
            toolRegistry.RegisterOrReplace(new ReadMcpResourceTool());
        }
    }

    private sealed class RegisteredMcpPromptCommandHandler : IMcpPromptCommandHandler
    {
        private const int MaxSessionRetries = 1;
        private readonly McpLifecycleManager _lifecycleManager;
        private readonly string _serverName;
        private readonly ScopedMcpServerConfig _config;
        private readonly McpPromptDefinition _prompt;
        private readonly string[] _argumentNames;

        public RegisteredMcpPromptCommandHandler(
            McpLifecycleManager lifecycleManager,
            string serverName,
            ScopedMcpServerConfig config,
            McpPromptDefinition prompt)
        {
            _lifecycleManager = lifecycleManager;
            _serverName = serverName;
            _config = config;
            _prompt = prompt;
            _argumentNames = (prompt.Arguments ?? []).Select(argument => argument.Name).ToArray();
            Descriptor = new CommandDescriptor(
                $"mcp__{McpToolRegistrationService.NormalizeNameForMcp(serverName)}__{prompt.Name}",
                prompt.Description ?? string.Empty,
                $"/mcp__{McpToolRegistrationService.NormalizeNameForMcp(serverName)}__{prompt.Name}");
        }

        public CommandDescriptor Descriptor { get; }

        public async Task<IReadOnlyList<ChatMessage>> GetPromptMessagesAsync(
            string arguments,
            CommandExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            var argsByName = ZipPromptArguments(arguments);
            for (var attempt = 0; ; attempt++)
            {
                var connection = await _lifecycleManager.ConnectToServerAsync(_serverName, _config, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (connection is not ConnectedMcpServerConnection connected)
                {
                    throw new InvalidOperationException($"MCP server '{_serverName}' is not connected.");
                }

                try
                {
                    var promptResult = await connected.Client.GetPromptAsync(_prompt.Name, argsByName, cancellationToken).ConfigureAwait(false);
                    return promptResult.Messages;
                }
                catch (Exception exception) when (attempt < MaxSessionRetries && ShouldRetry(exception))
                {
                    await _lifecycleManager.ClearServerCacheAsync(_serverName, _config, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private IReadOnlyDictionary<string, string?> ZipPromptArguments(string arguments)
        {
            var argsArray = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var zipped = new Dictionary<string, string?>(StringComparer.Ordinal);
            for (var index = 0; index < _argumentNames.Length; index++)
            {
                zipped[_argumentNames[index]] = index < argsArray.Length ? argsArray[index] : null;
            }

            return zipped;
        }

        private bool ShouldRetry(Exception exception)
        {
            return McpReconnectClassifier.IsSessionExpiredError(exception) ||
                   McpReconnectClassifier.IsConnectionClosedOnHttp(exception, _config);
        }
    }

}
