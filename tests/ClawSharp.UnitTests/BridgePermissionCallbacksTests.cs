// TS origin: ./bridge/bridgePermissionCallbacks.ts
using System.Text.Json.Nodes;
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgePermissionCallbacksTests
{
    [Fact]
    public void IsBridgePermissionResponse_Returns_True_For_Allow_And_Deny()
    {
        Assert.True(BridgePermissionCallbackUtilities.IsBridgePermissionResponse(
            JsonNode.Parse("""{"behavior":"allow"}""")));
        Assert.True(BridgePermissionCallbackUtilities.IsBridgePermissionResponse(
            JsonNode.Parse("""{"behavior":"deny"}""")));
    }

    [Fact]
    public void IsBridgePermissionResponse_Returns_False_For_Invalid_Payloads()
    {
        Assert.False(BridgePermissionCallbackUtilities.IsBridgePermissionResponse(null));
        Assert.False(BridgePermissionCallbackUtilities.IsBridgePermissionResponse(
            JsonNode.Parse("""{}""")));
        Assert.False(BridgePermissionCallbackUtilities.IsBridgePermissionResponse(
            JsonNode.Parse("""{"behavior":"maybe"}""")));
        Assert.False(BridgePermissionCallbackUtilities.IsBridgePermissionResponse(
            JsonNode.Parse("[]")));
        Assert.False(BridgePermissionCallbackUtilities.IsBridgePermissionResponse(
            JsonNode.Parse("\"allow\"")));
    }
}
