using ClawSharp.AgentHost.Contracts;

namespace ClawSharp.AgentHost.Services;

public interface IExternalEditorService
{
    Task<OpenExternalEditorResponse> OpenAsync(
        OpenExternalEditorRequest request,
        CancellationToken cancellationToken = default);
}
