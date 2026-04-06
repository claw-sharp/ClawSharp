using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Bridge;

public sealed record EnvLessBridgeConfig(
    int InitRetryMaxAttempts,
    int InitRetryBaseDelayMs,
    double InitRetryJitterFraction,
    int InitRetryMaxDelayMs,
    int HttpTimeoutMs,
    int UuidDedupBufferSize,
    int HeartbeatIntervalMs,
    double HeartbeatJitterFraction,
    int TokenRefreshBufferMs,
    int TeardownArchiveTimeoutMs,
    int ConnectTimeoutMs,
    string MinVersion,
    bool ShouldShowAppUpgradeMessage)
{
    public static EnvLessBridgeConfig Default { get; } = new(
        InitRetryMaxAttempts: 3,
        InitRetryBaseDelayMs: 500,
        InitRetryJitterFraction: 0.25,
        InitRetryMaxDelayMs: 4_000,
        HttpTimeoutMs: 10_000,
        UuidDedupBufferSize: 2_000,
        HeartbeatIntervalMs: 20_000,
        HeartbeatJitterFraction: 0.1,
        TokenRefreshBufferMs: 300_000,
        TeardownArchiveTimeoutMs: 1_500,
        ConnectTimeoutMs: 15_000,
        MinVersion: "0.0.0",
        ShouldShowAppUpgradeMessage: false);
}

public static class BridgeEnvLessConfigUtilities
{
    public static EnvLessBridgeConfig ParseOrDefault(
        JsonNode? raw,
        Func<string, bool> isValidVersion)
    {
        ArgumentNullException.ThrowIfNull(isValidVersion);

        if (raw is not JsonObject obj)
        {
            return EnvLessBridgeConfig.Default;
        }

        if (!TryReadInt(obj, "init_retry_max_attempts", EnvLessBridgeConfig.Default.InitRetryMaxAttempts, out var initRetryMaxAttempts) ||
            initRetryMaxAttempts < 1 ||
            initRetryMaxAttempts > 10)
        {
            return EnvLessBridgeConfig.Default;
        }

        if (!TryReadInt(obj, "init_retry_base_delay_ms", EnvLessBridgeConfig.Default.InitRetryBaseDelayMs, out var initRetryBaseDelayMs) ||
            initRetryBaseDelayMs < 100)
        {
            return EnvLessBridgeConfig.Default;
        }

        if (!TryReadDouble(obj, "init_retry_jitter_fraction", EnvLessBridgeConfig.Default.InitRetryJitterFraction, out var initRetryJitterFraction) ||
            initRetryJitterFraction < 0 ||
            initRetryJitterFraction > 1)
        {
            return EnvLessBridgeConfig.Default;
        }

        if (!TryReadInt(obj, "init_retry_max_delay_ms", EnvLessBridgeConfig.Default.InitRetryMaxDelayMs, out var initRetryMaxDelayMs) ||
            initRetryMaxDelayMs < 500)
        {
            return EnvLessBridgeConfig.Default;
        }

        if (!TryReadInt(obj, "http_timeout_ms", EnvLessBridgeConfig.Default.HttpTimeoutMs, out var httpTimeoutMs) ||
            httpTimeoutMs < 2_000)
        {
            return EnvLessBridgeConfig.Default;
        }

        if (!TryReadInt(obj, "uuid_dedup_buffer_size", EnvLessBridgeConfig.Default.UuidDedupBufferSize, out var uuidDedupBufferSize) ||
            uuidDedupBufferSize < 100 ||
            uuidDedupBufferSize > 50_000)
        {
            return EnvLessBridgeConfig.Default;
        }

        if (!TryReadInt(obj, "heartbeat_interval_ms", EnvLessBridgeConfig.Default.HeartbeatIntervalMs, out var heartbeatIntervalMs) ||
            heartbeatIntervalMs < 5_000 ||
            heartbeatIntervalMs > 30_000)
        {
            return EnvLessBridgeConfig.Default;
        }

        if (!TryReadDouble(obj, "heartbeat_jitter_fraction", EnvLessBridgeConfig.Default.HeartbeatJitterFraction, out var heartbeatJitterFraction) ||
            heartbeatJitterFraction < 0 ||
            heartbeatJitterFraction > 0.5)
        {
            return EnvLessBridgeConfig.Default;
        }

        if (!TryReadInt(obj, "token_refresh_buffer_ms", EnvLessBridgeConfig.Default.TokenRefreshBufferMs, out var tokenRefreshBufferMs) ||
            tokenRefreshBufferMs < 30_000 ||
            tokenRefreshBufferMs > 1_800_000)
        {
            return EnvLessBridgeConfig.Default;
        }

        if (!TryReadInt(obj, "teardown_archive_timeout_ms", EnvLessBridgeConfig.Default.TeardownArchiveTimeoutMs, out var teardownArchiveTimeoutMs) ||
            teardownArchiveTimeoutMs < 500 ||
            teardownArchiveTimeoutMs > 2_000)
        {
            return EnvLessBridgeConfig.Default;
        }

        if (!TryReadInt(obj, "connect_timeout_ms", EnvLessBridgeConfig.Default.ConnectTimeoutMs, out var connectTimeoutMs) ||
            connectTimeoutMs < 5_000 ||
            connectTimeoutMs > 60_000)
        {
            return EnvLessBridgeConfig.Default;
        }

        if (!TryReadString(obj, "min_version", EnvLessBridgeConfig.Default.MinVersion, out var minVersion) ||
            !isValidVersion(minVersion))
        {
            return EnvLessBridgeConfig.Default;
        }

        if (!TryReadBool(obj, "should_show_app_upgrade_message", EnvLessBridgeConfig.Default.ShouldShowAppUpgradeMessage, out var shouldShowAppUpgradeMessage))
        {
            return EnvLessBridgeConfig.Default;
        }

        return new EnvLessBridgeConfig(
            InitRetryMaxAttempts: initRetryMaxAttempts,
            InitRetryBaseDelayMs: initRetryBaseDelayMs,
            InitRetryJitterFraction: initRetryJitterFraction,
            InitRetryMaxDelayMs: initRetryMaxDelayMs,
            HttpTimeoutMs: httpTimeoutMs,
            UuidDedupBufferSize: uuidDedupBufferSize,
            HeartbeatIntervalMs: heartbeatIntervalMs,
            HeartbeatJitterFraction: heartbeatJitterFraction,
            TokenRefreshBufferMs: tokenRefreshBufferMs,
            TeardownArchiveTimeoutMs: teardownArchiveTimeoutMs,
            ConnectTimeoutMs: connectTimeoutMs,
            MinVersion: minVersion,
            ShouldShowAppUpgradeMessage: shouldShowAppUpgradeMessage);
    }

    public static async Task<EnvLessBridgeConfig> GetEnvLessBridgeConfigAsync(
        Func<Task<JsonNode?>> getRawConfigAsync,
        Func<string, bool> isValidVersion)
    {
        ArgumentNullException.ThrowIfNull(getRawConfigAsync);
        ArgumentNullException.ThrowIfNull(isValidVersion);

        var raw = await getRawConfigAsync();
        return ParseOrDefault(raw, isValidVersion);
    }

    public static async Task<string?> CheckEnvLessBridgeMinVersionAsync(
        Func<Task<EnvLessBridgeConfig>> getConfigAsync,
        string currentVersion,
        Func<string, string, bool> isVersionLessThan)
    {
        ArgumentNullException.ThrowIfNull(getConfigAsync);
        ArgumentNullException.ThrowIfNull(currentVersion);
        ArgumentNullException.ThrowIfNull(isVersionLessThan);

        var config = await getConfigAsync();
        if (!string.IsNullOrEmpty(config.MinVersion) &&
            isVersionLessThan(currentVersion, config.MinVersion))
        {
            return
                $"Your version of Claude Code ({currentVersion}) is too old for Remote Control.\n" +
                $"Version {config.MinVersion} or higher is required. Run `{AppMetadata.UpdateCommand}` to update.";
        }

        return null;
    }

    public static async Task<bool> ShouldShowAppUpgradeMessageAsync(
        Func<bool> isEnvLessBridgeEnabled,
        Func<Task<EnvLessBridgeConfig>> getConfigAsync)
    {
        ArgumentNullException.ThrowIfNull(isEnvLessBridgeEnabled);
        ArgumentNullException.ThrowIfNull(getConfigAsync);

        if (!isEnvLessBridgeEnabled())
        {
            return false;
        }

        var config = await getConfigAsync();
        return config.ShouldShowAppUpgradeMessage;
    }

    private static bool TryReadInt(JsonObject obj, string propertyName, int defaultValue, out int value)
    {
        value = defaultValue;
        if (!obj.TryGetPropertyValue(propertyName, out var node) || node is null)
        {
            return true;
        }

        return node is JsonValue jsonValue && jsonValue.TryGetValue(out value);
    }

    private static bool TryReadDouble(JsonObject obj, string propertyName, double defaultValue, out double value)
    {
        value = defaultValue;
        if (!obj.TryGetPropertyValue(propertyName, out var node) || node is null)
        {
            return true;
        }

        return node is JsonValue jsonValue && jsonValue.TryGetValue(out value);
    }

    private static bool TryReadString(JsonObject obj, string propertyName, string defaultValue, out string value)
    {
        value = defaultValue;
        if (!obj.TryGetPropertyValue(propertyName, out var node) || node is null)
        {
            return true;
        }

        return node is JsonValue jsonValue && jsonValue.TryGetValue(out value);
    }

    private static bool TryReadBool(JsonObject obj, string propertyName, bool defaultValue, out bool value)
    {
        value = defaultValue;
        if (!obj.TryGetPropertyValue(propertyName, out var node) || node is null)
        {
            return true;
        }

        return node is JsonValue jsonValue && jsonValue.TryGetValue(out value);
    }
}
