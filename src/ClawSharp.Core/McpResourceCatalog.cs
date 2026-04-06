// TS origin: ./services/mcp/client.ts, ./tools/ListMcpResourcesTool/ListMcpResourcesTool.ts
namespace ClawSharp.Core;

public sealed class McpResourceCatalog
{
    private readonly Dictionary<string, RegisteredMcpResourceSet> _resourcesByServer =
        new(StringComparer.Ordinal);

    public void RegisterOrReplace(
        string serverName,
        ScopedMcpServerConfig config,
        IReadOnlyList<McpServerResource> resources)
    {
        _resourcesByServer[serverName] = new RegisteredMcpResourceSet(serverName, config, resources);
    }

    public IReadOnlyList<McpServerResource> GetResources(string? serverName = null)
    {
        if (string.IsNullOrWhiteSpace(serverName))
        {
            return _resourcesByServer.Values
                .SelectMany(entry => entry.Resources)
                .OrderBy(resource => resource.Server, StringComparer.OrdinalIgnoreCase)
                .ThenBy(resource => resource.Uri, StringComparer.Ordinal)
                .ToArray();
        }

        return _resourcesByServer.TryGetValue(serverName, out var entry)
            ? entry.Resources
            : [];
    }

    public bool TryGetServer(string serverName, out RegisteredMcpResourceSet? resourceSet)
    {
        return _resourcesByServer.TryGetValue(serverName, out resourceSet);
    }

    public sealed record RegisteredMcpResourceSet(
        string ServerName,
        ScopedMcpServerConfig Config,
        IReadOnlyList<McpServerResource> Resources);
}
