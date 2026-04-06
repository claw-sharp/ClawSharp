using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed record BridgeTrustedDeviceTokenSourceDependencies(
    IMcpSecureStorage SecureStorage,
    Func<bool>? IsGateEnabled = null,
    Func<string, string?>? GetEnvironmentVariable = null);

public sealed class BridgeTrustedDeviceTokenSource
{
    private readonly IMcpSecureStorage _secureStorage;
    private readonly Func<bool> _isGateEnabled;
    private readonly Func<string, string?> _getEnvironmentVariable;

    public BridgeTrustedDeviceTokenSource(BridgeTrustedDeviceTokenSourceDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        _secureStorage = dependencies.SecureStorage ?? throw new ArgumentNullException(nameof(dependencies));
        _isGateEnabled = dependencies.IsGateEnabled ?? (() => false);
        _getEnvironmentVariable = dependencies.GetEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    }

    public string? GetTrustedDeviceToken()
    {
        if (!_isGateEnabled())
        {
            return null;
        }

        var envToken = _getEnvironmentVariable("CLAUDE_TRUSTED_DEVICE_TOKEN");
        if (!string.IsNullOrWhiteSpace(envToken))
        {
            return envToken;
        }

        return _secureStorage.Read()?.TrustedDeviceToken;
    }
}
