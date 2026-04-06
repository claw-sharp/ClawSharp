// TS origin: ./utils/secureStorage/index.ts
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class UnsupportedMcpSecureStorage : IMcpSecureStorage
{
    public McpSecureStorageData? Read() => null;

    public Task<McpSecureStorageData?> ReadAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Read());
    }

    public void Update(McpSecureStorageData data)
    {
        throw new NotSupportedException(
            "MCP secure storage is not implemented yet in ClawSharp. " +
            "A secure credential backend is required before MCP OAuth tokens can be persisted.");
    }

    public bool Delete() => false;
}
