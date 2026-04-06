// TS origin: ./utils/secureStorage/index.ts, ./utils/secureStorage/plainTextStorage.ts, ./utils/secureStorage/fallbackStorage.ts
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public static class McpSecureStorageFactory
{
    public static IMcpSecureStorage CreateDefault()
    {
        return CreateDefault(OperatingSystem.IsMacOS());
    }

    public static IMcpSecureStorage CreateDefault(
        bool isMacOs,
        IMcpSecureStorage? macOsPrimary = null,
        IMcpSecureStorage? secondary = null)
    {
        if (isMacOs)
        {
            return new FallbackMcpSecureStorage(
                macOsPrimary ?? new MacOsKeychainMcpSecureStorage(),
                secondary ?? new PlainTextMcpSecureStorage());
        }

        return secondary ?? new PlainTextMcpSecureStorage();
    }
}
