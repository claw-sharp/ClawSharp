// TS origin: ./utils/secureStorage/plainTextStorage.ts, ./utils/secureStorage/fallbackStorage.ts
using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class SecureStorageFoundationTests
{
    [Fact]
    public async Task FallbackStorage_Reads_Primary_First_And_Falls_Back_To_Secondary()
    {
        var primary = new InMemorySecureStorage();
        var secondary = new InMemorySecureStorage(
            new McpSecureStorageData(
                McpOAuth: new Dictionary<string, McpOAuthEntry>(StringComparer.Ordinal)
                {
                    ["server|secondary"] = new("server", "https://example.test", "secondary-token", 1)
                }));
        var storage = new FallbackMcpSecureStorage(primary, secondary);

        var result = await storage.ReadAsync();

        Assert.NotNull(result);
        Assert.Equal("secondary-token", result!.McpOAuth!["server|secondary"].AccessToken);

        primary.Update(
            new McpSecureStorageData(
                McpOAuth: new Dictionary<string, McpOAuthEntry>(StringComparer.Ordinal)
                {
                    ["server|primary"] = new("server", "https://example.test", "primary-token", 2)
                }));

        var primaryResult = storage.Read();
        Assert.Equal("primary-token", primaryResult!.McpOAuth!["server|primary"].AccessToken);
    }

    [Fact]
    public void FallbackStorage_Update_Deletes_Secondary_On_First_Primary_Write()
    {
        var primary = new InMemorySecureStorage();
        var secondary = new InMemorySecureStorage(new McpSecureStorageData());
        var storage = new FallbackMcpSecureStorage(primary, secondary);

        storage.Update(new McpSecureStorageData(
            McpOAuth: new Dictionary<string, McpOAuthEntry>(StringComparer.Ordinal)
            {
                ["server|1"] = new("server", "https://example.test", "token", 1)
            }));

        Assert.NotNull(primary.Read());
        Assert.Null(secondary.Read());
        Assert.Equal(1, secondary.DeleteCallCount);
    }

    [Fact]
    public void FallbackStorage_Update_Falls_Back_And_Deletes_Stale_Primary()
    {
        var primary = new InMemorySecureStorage(new McpSecureStorageData())
        {
            ThrowOnUpdate = true
        };
        var secondary = new InMemorySecureStorage();
        var storage = new FallbackMcpSecureStorage(primary, secondary);

        storage.Update(new McpSecureStorageData(
            McpOAuth: new Dictionary<string, McpOAuthEntry>(StringComparer.Ordinal)
            {
                ["server|2"] = new("server", "https://example.test", "fresh-token", 2)
            }));

        Assert.Equal("fresh-token", secondary.Read()!.McpOAuth!["server|2"].AccessToken);
        Assert.Null(primary.Read());
        Assert.Equal(1, primary.DeleteCallCount);
    }

    [Fact]
    public void PlainTextStorage_Update_Read_And_Delete_RoundTrip()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-secure-storage", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var path = Path.Combine(tempDir, ".credentials.json");
        var storage = new PlainTextMcpSecureStorage(path);

        try
        {
            storage.Update(
                new McpSecureStorageData(
                    McpOAuth: new Dictionary<string, McpOAuthEntry>(StringComparer.Ordinal)
                    {
                        ["server|3"] = new("server", "https://example.test", "access", 123, "refresh")
                    }));

            var loaded = storage.Read();
            Assert.NotNull(loaded);
            Assert.Equal("access", loaded!.McpOAuth!["server|3"].AccessToken);
            Assert.True(File.Exists(path));

            Assert.True(storage.Delete());
            Assert.False(File.Exists(path));
            Assert.Null(storage.Read());
            Assert.True(storage.Delete());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private sealed class InMemorySecureStorage : IMcpSecureStorage
    {
        private McpSecureStorageData? _data;

        public InMemorySecureStorage(McpSecureStorageData? data = null)
        {
            _data = data;
        }

        public bool ThrowOnUpdate { get; init; }

        public int DeleteCallCount { get; private set; }

        public McpSecureStorageData? Read() => _data;

        public Task<McpSecureStorageData?> ReadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_data);
        }

        public void Update(McpSecureStorageData data)
        {
            if (ThrowOnUpdate)
            {
                throw new InvalidOperationException("update failed");
            }

            _data = data;
        }

        public bool Delete()
        {
            DeleteCallCount++;
            _data = null;
            return true;
        }
    }
}
