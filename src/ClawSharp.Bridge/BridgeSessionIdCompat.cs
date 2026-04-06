namespace ClawSharp.Bridge;

public static class BridgeSessionIdCompat
{
    private static Func<bool>? _isCseShimEnabled;

    public static void SetCseShimGate(Func<bool> gate)
    {
        _isCseShimEnabled = gate ?? throw new ArgumentNullException(nameof(gate));
    }

    public static void ResetCseShimGate()
    {
        _isCseShimEnabled = null;
    }

    public static string ToCompatSessionId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (!id.StartsWith("cse_", StringComparison.Ordinal))
        {
            return id;
        }

        if (_isCseShimEnabled is not null && !_isCseShimEnabled())
        {
            return id;
        }

        return "session_" + id["cse_".Length..];
    }

    public static string ToInfraSessionId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (!id.StartsWith("session_", StringComparison.Ordinal))
        {
            return id;
        }

        return "cse_" + id["session_".Length..];
    }
}
