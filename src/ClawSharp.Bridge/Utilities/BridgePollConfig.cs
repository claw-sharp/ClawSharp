using System.Text.Json.Nodes;

namespace ClawSharp.Bridge;

public sealed record BridgePollConfig(
    int PollIntervalMsNotAtCapacity,
    int PollIntervalMsAtCapacity,
    int NonExclusiveHeartbeatIntervalMs,
    int MultisessionPollIntervalMsNotAtCapacity,
    int MultisessionPollIntervalMsPartialCapacity,
    int MultisessionPollIntervalMsAtCapacity,
    int ReclaimOlderThanMs,
    int SessionKeepaliveIntervalV2Ms)
{
    public static BridgePollConfig Default { get; } = new(
        PollIntervalMsNotAtCapacity: 2_000,
        PollIntervalMsAtCapacity: 600_000,
        NonExclusiveHeartbeatIntervalMs: 0,
        MultisessionPollIntervalMsNotAtCapacity: 2_000,
        MultisessionPollIntervalMsPartialCapacity: 2_000,
        MultisessionPollIntervalMsAtCapacity: 600_000,
        ReclaimOlderThanMs: 5_000,
        SessionKeepaliveIntervalV2Ms: 120_000);
}

public static class BridgePollConfigParser
{
    public static BridgePollConfig ParseOrDefault(JsonNode? raw)
    {
        if (raw is not JsonObject obj)
        {
            return BridgePollConfig.Default;
        }

        if (!TryReadInt(obj, "poll_interval_ms_not_at_capacity", out var pollIntervalMsNotAtCapacity) ||
            pollIntervalMsNotAtCapacity < 100)
        {
            return BridgePollConfig.Default;
        }

        if (!TryReadInt(obj, "poll_interval_ms_at_capacity", out var pollIntervalMsAtCapacity) ||
            !IsZeroOrAtLeast100(pollIntervalMsAtCapacity))
        {
            return BridgePollConfig.Default;
        }

        if (!TryReadIntWithDefault(
                obj,
                "non_exclusive_heartbeat_interval_ms",
                BridgePollConfig.Default.NonExclusiveHeartbeatIntervalMs,
                out var nonExclusiveHeartbeatIntervalMs) ||
            nonExclusiveHeartbeatIntervalMs < 0)
        {
            return BridgePollConfig.Default;
        }

        if (!TryReadIntWithDefault(
                obj,
                "multisession_poll_interval_ms_not_at_capacity",
                BridgePollConfig.Default.MultisessionPollIntervalMsNotAtCapacity,
                out var multisessionPollIntervalMsNotAtCapacity) ||
            multisessionPollIntervalMsNotAtCapacity < 100)
        {
            return BridgePollConfig.Default;
        }

        if (!TryReadIntWithDefault(
                obj,
                "multisession_poll_interval_ms_partial_capacity",
                BridgePollConfig.Default.MultisessionPollIntervalMsPartialCapacity,
                out var multisessionPollIntervalMsPartialCapacity) ||
            multisessionPollIntervalMsPartialCapacity < 100)
        {
            return BridgePollConfig.Default;
        }

        if (!TryReadIntWithDefault(
                obj,
                "multisession_poll_interval_ms_at_capacity",
                BridgePollConfig.Default.MultisessionPollIntervalMsAtCapacity,
                out var multisessionPollIntervalMsAtCapacity) ||
            !IsZeroOrAtLeast100(multisessionPollIntervalMsAtCapacity))
        {
            return BridgePollConfig.Default;
        }

        if (!TryReadIntWithDefault(
                obj,
                "reclaim_older_than_ms",
                BridgePollConfig.Default.ReclaimOlderThanMs,
                out var reclaimOlderThanMs) ||
            reclaimOlderThanMs < 1)
        {
            return BridgePollConfig.Default;
        }

        if (!TryReadIntWithDefault(
                obj,
                "session_keepalive_interval_v2_ms",
                BridgePollConfig.Default.SessionKeepaliveIntervalV2Ms,
                out var sessionKeepaliveIntervalV2Ms) ||
            sessionKeepaliveIntervalV2Ms < 0)
        {
            return BridgePollConfig.Default;
        }

        if (nonExclusiveHeartbeatIntervalMs <= 0 && pollIntervalMsAtCapacity <= 0)
        {
            return BridgePollConfig.Default;
        }

        if (nonExclusiveHeartbeatIntervalMs <= 0 && multisessionPollIntervalMsAtCapacity <= 0)
        {
            return BridgePollConfig.Default;
        }

        return new BridgePollConfig(
            PollIntervalMsNotAtCapacity: pollIntervalMsNotAtCapacity,
            PollIntervalMsAtCapacity: pollIntervalMsAtCapacity,
            NonExclusiveHeartbeatIntervalMs: nonExclusiveHeartbeatIntervalMs,
            MultisessionPollIntervalMsNotAtCapacity: multisessionPollIntervalMsNotAtCapacity,
            MultisessionPollIntervalMsPartialCapacity: multisessionPollIntervalMsPartialCapacity,
            MultisessionPollIntervalMsAtCapacity: multisessionPollIntervalMsAtCapacity,
            ReclaimOlderThanMs: reclaimOlderThanMs,
            SessionKeepaliveIntervalV2Ms: sessionKeepaliveIntervalV2Ms);
    }

    private static bool TryReadInt(JsonObject obj, string propertyName, out int value)
    {
        value = default;

        if (!obj.TryGetPropertyValue(propertyName, out var node) || node is null)
        {
            return false;
        }

        if (node is not JsonValue jsonValue)
        {
            return false;
        }

        return jsonValue.TryGetValue(out value);
    }

    private static bool TryReadIntWithDefault(JsonObject obj, string propertyName, int defaultValue, out int value)
    {
        if (!obj.TryGetPropertyValue(propertyName, out var node) || node is null)
        {
            value = defaultValue;
            return true;
        }

        if (node is not JsonValue jsonValue || !jsonValue.TryGetValue(out value))
        {
            value = default;
            return false;
        }

        return true;
    }

    private static bool IsZeroOrAtLeast100(int value)
    {
        return value == 0 || value >= 100;
    }
}
