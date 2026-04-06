// TS origin: ./utils/secureStorage/plainTextStorage.ts
using System.Text.Json;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class PlainTextMcpSecureStorage : IMcpSecureStorage
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _storagePath;

    public PlainTextMcpSecureStorage(string? storagePath = null)
    {
        _storagePath = storagePath ?? Path.Combine(SessionStoragePaths.GetClaudeConfigHomeDir(), ".credentials.json");
    }

    public McpSecureStorageData? Read()
    {
        if (!File.Exists(_storagePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<McpSecureStorageData>(File.ReadAllText(_storagePath), SerializerOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task<McpSecureStorageData?> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_storagePath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(_storagePath);
            return await JsonSerializer.DeserializeAsync<McpSecureStorageData>(stream, SerializerOptions, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public void Update(McpSecureStorageData data)
    {
        var directory = Path.GetDirectoryName(_storagePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_storagePath, JsonSerializer.Serialize(data, SerializerOptions));
        TryRestrictFilePermissions();
    }

    public bool Delete()
    {
        try
        {
            File.Delete(_storagePath);
            return true;
        }
        catch (FileNotFoundException)
        {
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void TryRestrictFilePermissions()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                _storagePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // TS plaintext storage ignores chmod failures and keeps the write.
        }
    }
}
