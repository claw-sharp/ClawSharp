// TS origin: ./bridge/flushGate.ts
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class FlushGateTests
{
    [Fact]
    public void Enqueue_Returns_False_When_Not_Active()
    {
        var gate = new FlushGate<string>();

        var enqueued = gate.Enqueue(["one"]);

        Assert.False(enqueued);
        Assert.Equal(0, gate.PendingCount);
        Assert.False(gate.Active);
    }

    [Fact]
    public void Start_And_End_Drain_Pending_Items()
    {
        var gate = new FlushGate<string>();
        gate.Start();

        Assert.True(gate.Enqueue(["one", "two"]));

        var drained = gate.End();

        Assert.False(gate.Active);
        Assert.Equal(["one", "two"], drained);
        Assert.Equal(0, gate.PendingCount);
    }

    [Fact]
    public void Drop_Clears_Pending_Items_And_Returns_Count()
    {
        var gate = new FlushGate<int>();
        gate.Start();
        Assert.True(gate.Enqueue([1, 2, 3]));

        var count = gate.Drop();

        Assert.Equal(3, count);
        Assert.False(gate.Active);
        Assert.Equal(0, gate.PendingCount);
    }

    [Fact]
    public void Deactivate_Clears_Active_Flag_Without_Dropping_Items()
    {
        var gate = new FlushGate<string>();
        gate.Start();
        Assert.True(gate.Enqueue(["queued"]));

        gate.Deactivate();

        Assert.False(gate.Active);
        Assert.Equal(1, gate.PendingCount);
        Assert.Equal(["queued"], gate.End());
    }
}
