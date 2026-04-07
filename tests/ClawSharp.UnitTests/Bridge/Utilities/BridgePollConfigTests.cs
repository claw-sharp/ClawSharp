using System.Text.Json.Nodes;
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgePollConfigTests
{
    [Fact]
    public void ParseOrDefault_Returns_Default_When_Input_Is_Null()
    {
        var result = BridgePollConfigParser.ParseOrDefault(null);

        Assert.Equal(BridgePollConfig.Default, result);
    }

    [Fact]
    public void ParseOrDefault_Applies_Ts_Defaults_For_Optional_Fields()
    {
        JsonObject raw = new()
        {
            ["poll_interval_ms_not_at_capacity"] = 2500,
            ["poll_interval_ms_at_capacity"] = 600000
        };

        var result = BridgePollConfigParser.ParseOrDefault(raw);

        Assert.Equal(2500, result.PollIntervalMsNotAtCapacity);
        Assert.Equal(600000, result.PollIntervalMsAtCapacity);
        Assert.Equal(BridgePollConfig.Default.NonExclusiveHeartbeatIntervalMs, result.NonExclusiveHeartbeatIntervalMs);
        Assert.Equal(BridgePollConfig.Default.MultisessionPollIntervalMsAtCapacity, result.MultisessionPollIntervalMsAtCapacity);
        Assert.Equal(BridgePollConfig.Default.ReclaimOlderThanMs, result.ReclaimOlderThanMs);
    }

    [Fact]
    public void ParseOrDefault_Falls_Back_Entirely_When_A_Required_Field_Violates_Minimum()
    {
        JsonObject raw = new()
        {
            ["poll_interval_ms_not_at_capacity"] = 99,
            ["poll_interval_ms_at_capacity"] = 600000
        };

        var result = BridgePollConfigParser.ParseOrDefault(raw);

        Assert.Equal(BridgePollConfig.Default, result);
    }

    [Fact]
    public void ParseOrDefault_Falls_Back_When_AtCapacity_Liveness_Is_Disabled_For_SingleSession()
    {
        JsonObject raw = new()
        {
            ["poll_interval_ms_not_at_capacity"] = 2000,
            ["poll_interval_ms_at_capacity"] = 0,
            ["non_exclusive_heartbeat_interval_ms"] = 0
        };

        var result = BridgePollConfigParser.ParseOrDefault(raw);

        Assert.Equal(BridgePollConfig.Default, result);
    }

    [Fact]
    public void ParseOrDefault_Falls_Back_When_AtCapacity_Liveness_Is_Disabled_For_MultiSession()
    {
        JsonObject raw = new()
        {
            ["poll_interval_ms_not_at_capacity"] = 2000,
            ["poll_interval_ms_at_capacity"] = 600000,
            ["non_exclusive_heartbeat_interval_ms"] = 0,
            ["multisession_poll_interval_ms_at_capacity"] = 0
        };

        var result = BridgePollConfigParser.ParseOrDefault(raw);

        Assert.Equal(BridgePollConfig.Default, result);
    }

    [Fact]
    public void ParseOrDefault_Accepts_Heartbeat_Only_AtCapacity_Liveness()
    {
        JsonObject raw = new()
        {
            ["poll_interval_ms_not_at_capacity"] = 2000,
            ["poll_interval_ms_at_capacity"] = 0,
            ["non_exclusive_heartbeat_interval_ms"] = 60000,
            ["multisession_poll_interval_ms_at_capacity"] = 0
        };

        var result = BridgePollConfigParser.ParseOrDefault(raw);

        Assert.Equal(60000, result.NonExclusiveHeartbeatIntervalMs);
        Assert.Equal(0, result.PollIntervalMsAtCapacity);
        Assert.Equal(0, result.MultisessionPollIntervalMsAtCapacity);
    }
}
