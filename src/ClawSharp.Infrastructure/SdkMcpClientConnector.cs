// TS origin: ./services/mcp/client.ts
using System.Net;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class SdkMcpClientConnector : IMcpClientConnector
{
    private readonly McpSdkHttpTransportFactory _transportFactory;
    private readonly McpNeedsAuthCache? _needsAuthCache;
    private readonly McpElicitationService? _elicitationService;
    private readonly Func<HttpClientTransportOptions, IClientTransport> _httpTransportBuilder;
    private readonly Func<Uri, IReadOnlyDictionary<string, string>?, IClientTransport> _webSocketTransportBuilder;
    private readonly Func<IClientTransport, CancellationToken, Task<IMcpClientSession>> _clientFactory;

    public SdkMcpClientConnector(
        McpSdkHttpTransportFactory transportFactory,
        McpNeedsAuthCache? needsAuthCache = null,
        McpElicitationService? elicitationService = null)
        : this(
            transportFactory,
            needsAuthCache,
            elicitationService,
            options => new HttpClientTransport(options),
            (uri, headers) => new WebSocketMcpClientTransport(uri, headers),
            CreateClientAsync)
    {
    }

    internal SdkMcpClientConnector(
        McpSdkHttpTransportFactory transportFactory,
        McpNeedsAuthCache? needsAuthCache,
        McpElicitationService? elicitationService,
        Func<HttpClientTransportOptions, IClientTransport> httpTransportBuilder,
        Func<Uri, IReadOnlyDictionary<string, string>?, IClientTransport> webSocketTransportBuilder,
        Func<IClientTransport, CancellationToken, Task<IMcpClientSession>> sessionFactory)
    {
        _transportFactory = transportFactory;
        _needsAuthCache = needsAuthCache;
        _elicitationService = elicitationService;
        _httpTransportBuilder = httpTransportBuilder;
        _webSocketTransportBuilder = webSocketTransportBuilder;
        _clientFactory = sessionFactory;
    }

    public async Task<McpServerConnection> ConnectAsync(
        string name,
        ScopedMcpServerConfig server,
        McpServerConnectionStatistics? serverStatistics = null,
        CancellationToken cancellationToken = default)
    {
        if (server.Type is not "http" and not "sse" and not "sse-ide" and not "ws-ide")
        {
            return new FailedMcpServerConnection(
                name,
                server,
                $"MCP transport connection is not implemented yet for server type '{server.Type}' in ClawSharp.");
        }

        var transport = await CreateTransportAsync(name, server, cancellationToken).ConfigureAwait(false);
        try
        {
            var session = await _clientFactory(transport, cancellationToken).ConfigureAwait(false);
            if (_elicitationService is not null)
            {
                _elicitationService.RegisterHandlers(session, name);
            }

            return new ConnectedMcpServerConnection(
                name,
                server,
                session,
                BuildCapabilities(session),
                CleanupAsync: () => session.DisposeAsync().AsTask(),
                ServerInfo: GetServerInfo(session),
                Instructions: GetInstructions(session));
        }
        catch (Exception exception) when (IsAuthFailure(exception))
        {
            if (_needsAuthCache is not null && (server.Type is "http" or "sse"))
            {
                await _needsAuthCache.SetEntryAsync(name, cancellationToken).ConfigureAwait(false);
            }

            return new NeedsAuthMcpServerConnection(name, server);
        }
        catch (Exception exception)
        {
            return new FailedMcpServerConnection(name, server, exception.Message);
        }
    }

    private async Task<IClientTransport> CreateTransportAsync(
        string name,
        ScopedMcpServerConfig server,
        CancellationToken cancellationToken)
    {
        switch (server.Config)
        {
            case McpHttpServerConfig:
            case McpSseServerConfig:
            {
                var options = await _transportFactory.CreateAsync(name, server, cancellationToken: cancellationToken).ConfigureAwait(false);
                return _httpTransportBuilder(options);
            }
            case McpSseIdeServerConfig sseIde:
            {
                return _httpTransportBuilder(
                    new HttpClientTransportOptions
                    {
                        Endpoint = new Uri(sseIde.Url, UriKind.Absolute),
                        Name = name,
                        TransportMode = HttpTransportMode.Sse
                    });
            }
            case McpWebSocketIdeServerConfig webSocketIde:
            {
                var headers = new Dictionary<string, string>(StringComparer.Ordinal);
                if (!string.IsNullOrWhiteSpace(webSocketIde.AuthToken))
                {
                    headers["X-Claude-Code-Ide-Authorization"] = webSocketIde.AuthToken;
                }

                return _webSocketTransportBuilder(
                    new Uri(webSocketIde.Url, UriKind.Absolute),
                    headers.Count == 0 ? null : headers);
            }
            default:
                throw new InvalidOperationException($"Unsupported MCP transport type '{server.Type}'.");
        }
    }

    private static async Task<IMcpClientSession> CreateClientAsync(IClientTransport transport, CancellationToken cancellationToken)
    {
        var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new SdkMcpClientSession(new SdkMcpClientFacade(client));
    }

    private static IReadOnlyDictionary<string, object?> BuildCapabilities(IMcpClientSession session)
    {
        if (session is not SdkMcpClientSession sdkSession)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        return BuildCapabilities(sdkSession.ServerCapabilities);
    }

    private static McpServerInfo? GetServerInfo(IMcpClientSession session)
    {
        if (session is not SdkMcpClientSession sdkSession)
        {
            return null;
        }

        return new McpServerInfo(sdkSession.ServerInfo.Name, sdkSession.ServerInfo.Version);
    }

    private static string? GetInstructions(IMcpClientSession session)
    {
        return session is SdkMcpClientSession sdkSession
            ? sdkSession.ServerInstructions
            : null;
    }

    private static IReadOnlyDictionary<string, object?> BuildCapabilities(ServerCapabilities capabilities)
    {
#pragma warning disable MCPEXP001
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (capabilities.Tools is not null)
        {
            result["tools"] = SerializeCapability(capabilities.Tools);
        }

        if (capabilities.Prompts is not null)
        {
            result["prompts"] = SerializeCapability(capabilities.Prompts);
        }

        if (capabilities.Resources is not null)
        {
            result["resources"] = SerializeCapability(capabilities.Resources);
        }

        if (capabilities.Logging is not null)
        {
            result["logging"] = SerializeCapability(capabilities.Logging);
        }

        if (capabilities.Completions is not null)
        {
            result["completions"] = SerializeCapability(capabilities.Completions);
        }

        if (capabilities.Experimental is not null)
        {
            result["experimental"] = capabilities.Experimental;
        }

        if (capabilities.Extensions is not null)
        {
            result["extensions"] = capabilities.Extensions;
        }

        if (capabilities.Tasks is not null)
        {
            result["tasks"] = SerializeCapability(capabilities.Tasks);
        }

        return result;
#pragma warning restore MCPEXP001
    }

    private static object? SerializeCapability<T>(T capability)
    {
        return JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(capability));
    }

    private static bool IsAuthFailure(Exception exception)
    {
        return exception switch
        {
            HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } => true,
            _ => false
        };
    }
}
