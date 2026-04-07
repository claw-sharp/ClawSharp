using System.Text.Json;
using ClawSharp.AgentHost.Contracts;
using ClawSharp.AgentHost.Projects;
using ClawSharp.AgentHost.Sessions;
using ClawSharp.Core;

namespace ClawSharp.AgentHost.Ipc;

public sealed class AgentHostCommandRouter
{
    private readonly ProjectCatalogService _projectCatalog;
    private readonly ThreadCatalogService _threadCatalog;

    public AgentHostCommandRouter(
        ProjectCatalogService projectCatalog,
        ThreadCatalogService threadCatalog)
    {
        _projectCatalog = projectCatalog;
        _threadCatalog = threadCatalog;
    }

    public async Task<object> ExecuteAsync(AgentHostRequestEnvelope request, CancellationToken cancellationToken)
    {
        return request.Command switch
        {
            "health" => CreateHealthResponse(),
            "openProject" => await _projectCatalog.OpenProjectAsync(DeserializePayload<OpenProjectRequest>(request), cancellationToken),
            "listRecentProjects" => await _projectCatalog.ListRecentProjectsAsync(cancellationToken),
            "listThreads" => await _threadCatalog.ListThreadsAsync(DeserializePayload<ListThreadsRequest>(request), cancellationToken),
            "createThread" => await _threadCatalog.CreateThreadAsync(DeserializePayload<CreateThreadRequest>(request), cancellationToken),
            "getThread" => await _threadCatalog.GetThreadAsync(DeserializePayload<GetThreadRequest>(request), cancellationToken),
            _ => throw new AgentHostException("unsupported_command", $"Unsupported AgentHost command '{request.Command}'.")
        };
    }

    private static HealthResponse CreateHealthResponse()
    {
        return new HealthResponse(
            HostName: $"{AppMetadata.Name}.AgentHost",
            HostVersion: AppMetadata.Version,
            ProtocolVersion: AgentHostProtocol.ProtocolVersion,
            CurrentWorkingDirectory: Directory.GetCurrentDirectory(),
            SupportedCommands: AgentHostProtocol.SupportedCommands);
    }

    private static TRequest DeserializePayload<TRequest>(AgentHostRequestEnvelope request)
        where TRequest : class, new()
    {
        if (request.Payload is null ||
            request.Payload.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return new TRequest();
        }

        var payload = JsonSerializer.Deserialize<TRequest>(
            request.Payload.Value.GetRawText(),
            AgentHostProtocol.JsonOptions);

        return payload ?? new TRequest();
    }
}
