namespace ClawSharp.Bridge;

public interface IReplBridgeHandle
{
    string BridgeSessionId { get; }
}

public static class ReplBridgeHandleRegistry
{
    private static IReplBridgeHandle? _handle;
    private static Func<string?, Task>? _publishSessionBridgeIdAsync;

    public static void SetSessionBridgeIdPublisher(Func<string?, Task>? publisher)
    {
        _publishSessionBridgeIdAsync = publisher;
    }

    public static void SetReplBridgeHandle(IReplBridgeHandle? handle)
    {
        _handle = handle;

        try
        {
            _ = _publishSessionBridgeIdAsync?.Invoke(GetSelfBridgeCompatId());
        }
        catch
        {
            // Best-effort publication only; TS swallows updateSessionBridgeId failures.
        }
    }

    public static IReplBridgeHandle? GetReplBridgeHandle()
    {
        return _handle;
    }

    public static string? GetSelfBridgeCompatId()
    {
        return _handle is null
            ? null
            : BridgeSessionIdCompat.ToCompatSessionId(_handle.BridgeSessionId);
    }
}
