// TS origin: ./bridge/trustedDevice.ts
using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class BridgeTrustedDeviceTokenSourceTests
{
    [Fact]
    public void GetTrustedDeviceToken_Returns_Null_When_Gate_Is_Disabled()
    {
        var source = new BridgeTrustedDeviceTokenSource(
            new BridgeTrustedDeviceTokenSourceDependencies(
                new StubSecureStorage(new McpSecureStorageData(TrustedDeviceToken: "stored-token")),
                IsGateEnabled: () => false));

        Assert.Null(source.GetTrustedDeviceToken());
    }

    [Fact]
    public void GetTrustedDeviceToken_Prefers_Environment_Over_Storage()
    {
        var source = new BridgeTrustedDeviceTokenSource(
            new BridgeTrustedDeviceTokenSourceDependencies(
                new StubSecureStorage(new McpSecureStorageData(TrustedDeviceToken: "stored-token")),
                IsGateEnabled: () => true,
                GetEnvironmentVariable: name => name == "CLAUDE_TRUSTED_DEVICE_TOKEN" ? "env-token" : null));

        Assert.Equal("env-token", source.GetTrustedDeviceToken());
    }

    [Fact]
    public void GetTrustedDeviceToken_Reads_Secure_Storage_When_Gated_On()
    {
        var source = new BridgeTrustedDeviceTokenSource(
            new BridgeTrustedDeviceTokenSourceDependencies(
                new StubSecureStorage(new McpSecureStorageData(TrustedDeviceToken: "stored-token")),
                IsGateEnabled: () => true,
                GetEnvironmentVariable: _ => null));

        Assert.Equal("stored-token", source.GetTrustedDeviceToken());
    }

    private sealed class StubSecureStorage(McpSecureStorageData? data) : IMcpSecureStorage
    {
        public McpSecureStorageData? Read() => data;

        public Task<McpSecureStorageData?> ReadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(data);

        public void Update(McpSecureStorageData data)
        {
        }

        public bool Delete() => true;
    }
}
