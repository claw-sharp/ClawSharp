// TS origin: ./utils/secureStorage/index.ts, ./utils/secureStorage/types.ts
namespace ClawSharp.Core;

public interface IMcpSecureStorage
{
    McpSecureStorageData? Read();

    Task<McpSecureStorageData?> ReadAsync(CancellationToken cancellationToken = default);

    void Update(McpSecureStorageData data);

    bool Delete();
}
