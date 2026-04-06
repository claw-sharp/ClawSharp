using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

[Collection("TelemetrySerial")]
public sealed class BridgeDebugStateTests
{
    [Fact]
    public void Register_And_Clear_Debug_Handle_Tracks_Module_Level_Handle()
    {
        BridgeDebugState.ClearBridgeDebugHandle();
        var handle = new StubBridgeDebugHandle();

        BridgeDebugState.RegisterBridgeDebugHandle(handle);

        Assert.Same(handle, BridgeDebugState.GetBridgeDebugHandle());

        BridgeDebugState.ClearBridgeDebugHandle();

        Assert.Null(BridgeDebugState.GetBridgeDebugHandle());
    }

    [Fact]
    public void Inject_And_Consume_Fault_Decrements_Count_And_Removes_At_Zero()
    {
        BridgeDebugState.ClearBridgeDebugHandle();
        BridgeDebugState.InjectBridgeFault(new BridgeFault("pollForWork", "fatal", 404, "not_found_error", 2));

        var first = BridgeDebugState.TryConsumeFault("pollForWork");
        var second = BridgeDebugState.TryConsumeFault("pollForWork");
        var third = BridgeDebugState.TryConsumeFault("pollForWork");

        Assert.NotNull(first);
        Assert.Equal(2, first!.Count);
        Assert.NotNull(second);
        Assert.Equal(1, second!.Count);
        Assert.Null(third);
    }

    [Fact]
    public void Consume_Fault_Matches_First_Queued_Fault_By_Method()
    {
        BridgeDebugState.ClearBridgeDebugHandle();
        BridgeDebugState.InjectBridgeFault(new BridgeFault("heartbeatWork", "transient", 500, Count: 1));
        BridgeDebugState.InjectBridgeFault(new BridgeFault("pollForWork", "fatal", 404, Count: 1));

        var consumed = BridgeDebugState.TryConsumeFault("pollForWork");
        var untouched = BridgeDebugState.TryConsumeFault("heartbeatWork");

        Assert.NotNull(consumed);
        Assert.Equal("pollForWork", consumed!.Method);
        Assert.NotNull(untouched);
        Assert.Equal("heartbeatWork", untouched!.Method);
    }

    [Fact]
    public void Clear_Debug_Handle_Also_Clears_Fault_Queue()
    {
        BridgeDebugState.ClearBridgeDebugHandle();
        BridgeDebugState.InjectBridgeFault(new BridgeFault("reconnectSession", "transient", 503, Count: 1));

        BridgeDebugState.ClearBridgeDebugHandle();

        Assert.Null(BridgeDebugState.TryConsumeFault("reconnectSession"));
    }

    private sealed class StubBridgeDebugHandle : IBridgeDebugHandle
    {
        public void FireClose(int code)
        {
        }

        public void ForceReconnect()
        {
        }

        public void InjectFault(BridgeFault fault)
        {
        }

        public void WakePollLoop()
        {
        }

        public string Describe()
        {
            return "stub";
        }
    }
}
