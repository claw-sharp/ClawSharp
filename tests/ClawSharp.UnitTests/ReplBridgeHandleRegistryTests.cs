// TS origin: ./bridge/replBridgeHandle.ts
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class ReplBridgeHandleRegistryTests
{
    [Fact]
    public void SetAndGetReplBridgeHandle_Tracks_Global_Handle()
    {
        ReplBridgeHandleRegistry.SetSessionBridgeIdPublisher(null);
        ReplBridgeHandleRegistry.SetReplBridgeHandle(null);

        var handle = new TestReplBridgeHandle("cse_123");
        ReplBridgeHandleRegistry.SetReplBridgeHandle(handle);

        Assert.Same(handle, ReplBridgeHandleRegistry.GetReplBridgeHandle());
        Assert.Equal("session_123", ReplBridgeHandleRegistry.GetSelfBridgeCompatId());
    }

    [Fact]
    public async Task SetReplBridgeHandle_Publishes_Compat_Id_And_Clear_Null()
    {
        List<string?> published = [];
        ReplBridgeHandleRegistry.SetSessionBridgeIdPublisher(id =>
        {
            published.Add(id);
            return Task.CompletedTask;
        });

        ReplBridgeHandleRegistry.SetReplBridgeHandle(new TestReplBridgeHandle("cse_abc"));
        ReplBridgeHandleRegistry.SetReplBridgeHandle(null);

        await Task.Yield();

        Assert.Equal(["session_abc", null], published);
    }

    [Fact]
    public async Task SetReplBridgeHandle_Swallows_Publisher_Failures()
    {
        ReplBridgeHandleRegistry.SetSessionBridgeIdPublisher(_ => throw new InvalidOperationException("boom"));

        var exception = await Record.ExceptionAsync(async () =>
        {
            ReplBridgeHandleRegistry.SetReplBridgeHandle(new TestReplBridgeHandle("cse_fail"));
            await Task.Yield();
        });

        Assert.Null(exception);
        Assert.Equal("session_fail", ReplBridgeHandleRegistry.GetSelfBridgeCompatId());
    }

    private sealed record TestReplBridgeHandle(string BridgeSessionId) : IReplBridgeHandle;
}
