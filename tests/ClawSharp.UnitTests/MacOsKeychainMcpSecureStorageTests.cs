using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class MacOsKeychainMcpSecureStorageTests
{
    [Fact]
    public void Read_Uses_Ts_Shaped_Service_Name_And_Parses_Stored_Json()
    {
        MacOsKeychainProcessRequest? recordedRequest = null;
        var storage = new MacOsKeychainMcpSecureStorage(
            new MacOsKeychainMcpSecureStorageDependencies(
                Execute: request =>
                {
                    recordedRequest = request;
                    return new ProcessExecutionResult(
                        0,
                        JsonSerializer.Serialize(new McpSecureStorageData(TrustedDeviceToken: "device-token")),
                        string.Empty);
                },
                GetClaudeConfigHomeDir: () => "/tmp/custom-claude",
                GetEnvironmentVariable: name => name switch
                {
                    "CLAUDE_CONFIG_DIR" => "/tmp/custom-claude",
                    "USER_TYPE" => "ant",
                    "USE_STAGING_OAUTH" => "1",
                    _ => null
                },
                GetUsername: () => "mac-user"));

        var result = storage.Read();
        var expectedHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes("/tmp/custom-claude")))
            .ToLowerInvariant()[..8];

        Assert.NotNull(result);
        Assert.Equal("device-token", result!.TrustedDeviceToken);
        Assert.NotNull(recordedRequest);
        Assert.Equal("security", recordedRequest!.FileName);
        Assert.Equal("find-generic-password", recordedRequest.Arguments[0]);
        Assert.Equal("mac-user", recordedRequest.Arguments[2]);
        Assert.Equal($"Claude Code-staging-oauth-credentials-{expectedHash}", recordedRequest.Arguments[5]);
    }

    [Fact]
    public void Update_Uses_SecurityInteractive_Stdin_For_Short_Payloads()
    {
        MacOsKeychainProcessRequest? recordedRequest = null;
        var storage = new MacOsKeychainMcpSecureStorage(
            new MacOsKeychainMcpSecureStorageDependencies(
                Execute: request =>
                {
                    recordedRequest = request;
                    return new ProcessExecutionResult(0, string.Empty, string.Empty);
                },
                GetEnvironmentVariable: _ => null,
                GetUsername: () => "mac-user"));

        storage.Update(new McpSecureStorageData(TrustedDeviceToken: "short-token"));

        Assert.NotNull(recordedRequest);
        Assert.Equal(["-i"], recordedRequest!.Arguments);
        Assert.NotNull(recordedRequest.Stdin);
        Assert.Contains("add-generic-password -U -a \"mac-user\"", recordedRequest.Stdin, StringComparison.Ordinal);
        Assert.Contains("Claude Code-credentials", recordedRequest.Stdin, StringComparison.Ordinal);
    }

    [Fact]
    public void Update_Uses_Argv_Fallback_For_Large_Payloads()
    {
        MacOsKeychainProcessRequest? recordedRequest = null;
        var storage = new MacOsKeychainMcpSecureStorage(
            new MacOsKeychainMcpSecureStorageDependencies(
                Execute: request =>
                {
                    recordedRequest = request;
                    return new ProcessExecutionResult(0, string.Empty, string.Empty);
                },
                GetEnvironmentVariable: _ => null,
                GetUsername: () => "mac-user"));

        storage.Update(
            new McpSecureStorageData(
                PluginSecrets: new Dictionary<string, IReadOnlyDictionary<string, string>>
                {
                    ["plugin"] = new Dictionary<string, string>
                    {
                        ["secret"] = new string('x', 8_000)
                    }
                }));

        Assert.NotNull(recordedRequest);
        Assert.Null(recordedRequest!.Stdin);
        Assert.Equal("add-generic-password", recordedRequest.Arguments[0]);
        Assert.Equal("-X", recordedRequest.Arguments[^2]);
        Assert.True(recordedRequest.Arguments[^1].Length > 8_000);
    }

    [Fact]
    public async Task ReadAsync_Deduplicates_InFlight_Keychain_Reads()
    {
        var callCount = 0;
        var completion = new TaskCompletionSource<ProcessExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new MacOsKeychainMcpSecureStorage(
            new MacOsKeychainMcpSecureStorageDependencies(
                Execute: _ => throw new NotSupportedException(),
                ExecuteAsync: async (request, cancellationToken) =>
                {
                    callCount++;
                    return await completion.Task.WaitAsync(cancellationToken);
                },
                GetEnvironmentVariable: _ => null,
                GetUsername: () => "mac-user"));

        var first = storage.ReadAsync();
        var second = storage.ReadAsync();
        completion.SetResult(
            new ProcessExecutionResult(
                0,
                JsonSerializer.Serialize(new McpSecureStorageData(TrustedDeviceToken: "async-token")),
                string.Empty));

        var firstResult = await first;
        var secondResult = await second;

        Assert.Equal(1, callCount);
        Assert.Equal("async-token", firstResult!.TrustedDeviceToken);
        Assert.Equal("async-token", secondResult!.TrustedDeviceToken);
    }

    [Theory]
    [InlineData(true, typeof(FallbackMcpSecureStorage))]
    [InlineData(false, typeof(PlainTextMcpSecureStorage))]
    public void Factory_CreateDefault_Matches_Ts_Platform_Branch(bool isMacOs, Type expectedType)
    {
        var storage = McpSecureStorageFactory.CreateDefault(
            isMacOs,
            macOsPrimary: new UnsupportedMcpSecureStorage(),
            secondary: new PlainTextMcpSecureStorage(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json")));

        Assert.IsType(expectedType, storage);
    }
}
