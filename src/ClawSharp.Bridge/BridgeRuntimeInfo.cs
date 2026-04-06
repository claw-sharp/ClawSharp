namespace ClawSharp.Bridge;

public sealed class BridgeRuntimeInfo
{
    public string Mode => "Foundation";
    public string Notes =>
        "Bridge startup, session registration, poll loops, transport reconnect, session heartbeat, remote-control event routing, remote session manager wiring, direct-connect bootstrapping, capacity wake, and hidden child-session transport/runtime are ported.";
}
