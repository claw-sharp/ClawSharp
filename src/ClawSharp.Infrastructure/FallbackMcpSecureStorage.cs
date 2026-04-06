using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class FallbackMcpSecureStorage : IMcpSecureStorage
{
    private readonly IMcpSecureStorage _primary;
    private readonly IMcpSecureStorage _secondary;

    public FallbackMcpSecureStorage(IMcpSecureStorage primary, IMcpSecureStorage secondary)
    {
        _primary = primary;
        _secondary = secondary;
    }

    public McpSecureStorageData? Read()
    {
        var result = _primary.Read();
        if (result is not null)
        {
            return result;
        }

        return _secondary.Read() ?? new McpSecureStorageData();
    }

    public async Task<McpSecureStorageData?> ReadAsync(CancellationToken cancellationToken = default)
    {
        var result = await _primary.ReadAsync(cancellationToken);
        if (result is not null)
        {
            return result;
        }

        return await _secondary.ReadAsync(cancellationToken) ?? new McpSecureStorageData();
    }

    public void Update(McpSecureStorageData data)
    {
        var primaryDataBefore = _primary.Read();

        try
        {
            _primary.Update(data);
            if (primaryDataBefore is null)
            {
                _secondary.Delete();
            }

            return;
        }
        catch
        {
            try
            {
                _secondary.Update(data);
            }
            catch
            {
                throw;
            }

            if (primaryDataBefore is not null)
            {
                _primary.Delete();
            }
        }
    }

    public bool Delete()
    {
        var primarySuccess = _primary.Delete();
        var secondarySuccess = _secondary.Delete();
        return primarySuccess || secondarySuccess;
    }
}
