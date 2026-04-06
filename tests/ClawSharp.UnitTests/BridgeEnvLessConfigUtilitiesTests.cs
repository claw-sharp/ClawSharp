// TS origin: ./bridge/envLessBridgeConfig.ts
using System.Text.Json.Nodes;
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgeEnvLessConfigUtilitiesTests
{
    [Fact]
    public void ParseOrDefault_Returns_Default_For_Non_Object_Input()
    {
        var parsed = BridgeEnvLessConfigUtilities.ParseOrDefault(
            JsonNode.Parse("[]"),
            IsValidVersion);

        Assert.Equal(EnvLessBridgeConfig.Default, parsed);
    }

    [Fact]
    public void ParseOrDefault_Applies_Defaults_For_Missing_Optional_Fields()
    {
        var parsed = BridgeEnvLessConfigUtilities.ParseOrDefault(
            JsonNode.Parse(
                """
                {
                  "init_retry_max_attempts": 4,
                  "init_retry_base_delay_ms": 900,
                  "init_retry_jitter_fraction": 0.4,
                  "init_retry_max_delay_ms": 7000,
                  "http_timeout_ms": 12000,
                  "uuid_dedup_buffer_size": 4000,
                  "heartbeat_interval_ms": 25000
                }
                """),
            IsValidVersion);

        Assert.Equal(4, parsed.InitRetryMaxAttempts);
        Assert.Equal(900, parsed.InitRetryBaseDelayMs);
        Assert.Equal(0.4, parsed.InitRetryJitterFraction);
        Assert.Equal(7_000, parsed.InitRetryMaxDelayMs);
        Assert.Equal(12_000, parsed.HttpTimeoutMs);
        Assert.Equal(4_000, parsed.UuidDedupBufferSize);
        Assert.Equal(25_000, parsed.HeartbeatIntervalMs);
        Assert.Equal(EnvLessBridgeConfig.Default.HeartbeatJitterFraction, parsed.HeartbeatJitterFraction);
        Assert.Equal(EnvLessBridgeConfig.Default.TokenRefreshBufferMs, parsed.TokenRefreshBufferMs);
        Assert.Equal(EnvLessBridgeConfig.Default.TeardownArchiveTimeoutMs, parsed.TeardownArchiveTimeoutMs);
        Assert.Equal(EnvLessBridgeConfig.Default.ConnectTimeoutMs, parsed.ConnectTimeoutMs);
        Assert.Equal(EnvLessBridgeConfig.Default.MinVersion, parsed.MinVersion);
        Assert.Equal(EnvLessBridgeConfig.Default.ShouldShowAppUpgradeMessage, parsed.ShouldShowAppUpgradeMessage);
    }

    [Fact]
    public void ParseOrDefault_Rejects_Whole_Object_On_Invalid_Field()
    {
        var parsed = BridgeEnvLessConfigUtilities.ParseOrDefault(
            JsonNode.Parse(
                """
                {
                  "init_retry_max_attempts": 4,
                  "init_retry_base_delay_ms": 900,
                  "init_retry_jitter_fraction": 0.4,
                  "init_retry_max_delay_ms": 7000,
                  "http_timeout_ms": 12000,
                  "uuid_dedup_buffer_size": 4000,
                  "heartbeat_interval_ms": 25000,
                  "heartbeat_jitter_fraction": 0.75
                }
                """),
            IsValidVersion);

        Assert.Equal(EnvLessBridgeConfig.Default, parsed);
    }

    [Fact]
    public void ParseOrDefault_Rejects_Invalid_Min_Version_Through_Injected_Validator()
    {
        var parsed = BridgeEnvLessConfigUtilities.ParseOrDefault(
            JsonNode.Parse(
                """
                {
                  "min_version": "not-a-semver"
                }
                """),
            IsValidVersion);

        Assert.Equal(EnvLessBridgeConfig.Default, parsed);
    }

    [Fact]
    public async Task GetEnvLessBridgeConfigAsync_Returns_Default_When_Parsed_Config_Is_Invalid()
    {
        var parsed = await BridgeEnvLessConfigUtilities.GetEnvLessBridgeConfigAsync(
            () => Task.FromResult(JsonNode.Parse("""{"connect_timeout_ms": 1000}""")),
            IsValidVersion);

        Assert.Equal(EnvLessBridgeConfig.Default, parsed);
    }

    [Fact]
    public async Task CheckEnvLessBridgeMinVersionAsync_Returns_Ts_Shaped_Error_Message_When_Current_Version_Is_Too_Old()
    {
        var message = await BridgeEnvLessConfigUtilities.CheckEnvLessBridgeMinVersionAsync(
            () => Task.FromResult(EnvLessBridgeConfig.Default with { MinVersion = "1.2.3" }),
            "1.2.2",
            IsVersionLessThan);

        Assert.Equal(
            "Your version of Claude Code (1.2.2) is too old for Remote Control.\nVersion 1.2.3 or higher is required. Run `clawsharp update` to update.",
            message);
    }

    [Fact]
    public async Task CheckEnvLessBridgeMinVersionAsync_Returns_Null_When_Current_Version_Meets_Minimum()
    {
        var message = await BridgeEnvLessConfigUtilities.CheckEnvLessBridgeMinVersionAsync(
            () => Task.FromResult(EnvLessBridgeConfig.Default with { MinVersion = "1.2.3" }),
            "1.2.3",
            IsVersionLessThan);

        Assert.Null(message);
    }

    [Fact]
    public async Task ShouldShowAppUpgradeMessageAsync_Requires_EnvLess_Bridge_To_Be_Enabled()
    {
        var shouldShow = await BridgeEnvLessConfigUtilities.ShouldShowAppUpgradeMessageAsync(
            () => false,
            () => Task.FromResult(EnvLessBridgeConfig.Default with { ShouldShowAppUpgradeMessage = true }));

        Assert.False(shouldShow);
    }

    [Fact]
    public async Task ShouldShowAppUpgradeMessageAsync_Returns_Config_Bit_When_Enabled()
    {
        var shouldShow = await BridgeEnvLessConfigUtilities.ShouldShowAppUpgradeMessageAsync(
            () => true,
            () => Task.FromResult(EnvLessBridgeConfig.Default with { ShouldShowAppUpgradeMessage = true }));

        Assert.True(shouldShow);
    }

    private static bool IsValidVersion(string value)
    {
        return value.Split('.').Length == 3 && value.Split('.').All(part => int.TryParse(part, out _));
    }

    private static bool IsVersionLessThan(string left, string right)
    {
        static int[] Parse(string value) => value.Split('.').Select(int.Parse).ToArray();

        var leftParts = Parse(left);
        var rightParts = Parse(right);
        for (var index = 0; index < Math.Min(leftParts.Length, rightParts.Length); index++)
        {
            if (leftParts[index] != rightParts[index])
            {
                return leftParts[index] < rightParts[index];
            }
        }

        return leftParts.Length < rightParts.Length;
    }
}
