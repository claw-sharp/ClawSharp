// TS origin: ./bridge/capacityWake.ts
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class CapacityWakeTests
{
    [Fact]
    public void CreateSignal_Returns_AlreadyCanceled_Token_When_Outer_Token_Is_Canceled()
    {
        using var outerSource = new CancellationTokenSource();
        outerSource.Cancel();
        var wake = new CapacityWake(outerSource.Token);

        var signal = wake.CreateSignal();

        Assert.True(signal.Token.IsCancellationRequested);
        signal.Cleanup();
    }

    [Fact]
    public void Wake_Cancels_Current_Signal_But_Not_Future_Signals()
    {
        using var outerSource = new CancellationTokenSource();
        var wake = new CapacityWake(outerSource.Token);

        var first = wake.CreateSignal();

        Assert.False(first.Token.IsCancellationRequested);

        wake.Wake();

        Assert.True(first.Token.IsCancellationRequested);

        var second = wake.CreateSignal();
        Assert.False(second.Token.IsCancellationRequested);

        first.Cleanup();
        second.Cleanup();
    }

    [Fact]
    public void Outer_Cancellation_Cancels_Merged_Signal()
    {
        using var outerSource = new CancellationTokenSource();
        var wake = new CapacityWake(outerSource.Token);
        var signal = wake.CreateSignal();

        outerSource.Cancel();

        Assert.True(signal.Token.IsCancellationRequested);
        signal.Cleanup();
    }

    [Fact]
    public void Cleanup_Does_Not_Prevent_Future_Wake_Cycles()
    {
        using var outerSource = new CancellationTokenSource();
        var wake = new CapacityWake(outerSource.Token);

        var first = wake.CreateSignal();
        first.Cleanup();

        wake.Wake();

        var second = wake.CreateSignal();

        Assert.False(second.Token.IsCancellationRequested);
        second.Cleanup();
    }
}
