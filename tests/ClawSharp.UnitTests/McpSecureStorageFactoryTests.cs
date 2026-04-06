// TS origin: ./utils/secureStorage/index.ts, ./utils/secureStorage/plainTextStorage.ts
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class McpSecureStorageFactoryTests
{
    [Fact]
    public void CreateDefault_Uses_Plaintext_Storage_On_NonMacOS()
    {
        if (OperatingSystem.IsMacOS())
        {
            return;
        }

        var storage = McpSecureStorageFactory.CreateDefault();

        Assert.IsType<PlainTextMcpSecureStorage>(storage);
    }

    [Fact]
    public void CreateDefault_Does_Not_Use_Unsupported_Storage_On_NonMacOS()
    {
        if (OperatingSystem.IsMacOS())
        {
            return;
        }

        var storage = McpSecureStorageFactory.CreateDefault();

        Assert.IsNotType<UnsupportedMcpSecureStorage>(storage);
    }
}
