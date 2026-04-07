using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgeStatusUtilitiesTests
{
    [Fact]
    public void ToCompatSessionId_Translates_Cse_Id_When_Shared_Shim_Is_Enabled()
    {
        BridgeSessionIdCompat.SetCseShimGate(() => true);
        try
        {
            var result = BridgeSessionIdCompat.ToCompatSessionId("cse_123");
            Assert.Equal("session_123", result);
        }
        finally
        {
            BridgeSessionIdCompat.ResetCseShimGate();
        }
    }

    [Fact]
    public void ToCompatSessionId_Leaves_Cse_Id_Alone_When_Shared_Shim_Is_Disabled()
    {
        BridgeSessionIdCompat.SetCseShimGate(() => false);
        try
        {
            var result = BridgeSessionIdCompat.ToCompatSessionId("cse_123");
            Assert.Equal("cse_123", result);
        }
        finally
        {
            BridgeSessionIdCompat.ResetCseShimGate();
        }
    }

    [Fact]
    public void ToInfraSessionId_Translates_Session_Id_Back_To_Cse_Id()
    {
        var result = BridgeSessionIdCompat.ToInfraSessionId("session_123");

        Assert.Equal("cse_123", result);
    }

    [Fact]
    public void GetClaudeAiBaseUrl_Uses_Local_And_Staging_Detection_From_Ts()
    {
        Assert.Equal(
            BridgeProductUrls.ClaudeAiLocalBaseUrl,
            BridgeProductUrls.GetClaudeAiBaseUrl("session_local_123", null));
        Assert.Equal(
            BridgeProductUrls.ClaudeAiStagingBaseUrl,
            BridgeProductUrls.GetClaudeAiBaseUrl("session_staging_123", null));
        Assert.Equal(
            BridgeProductUrls.ClaudeAiBaseUrl,
            BridgeProductUrls.GetClaudeAiBaseUrl("session_123", null));
    }

    [Fact]
    public void BuildBridgeUrls_Use_Product_And_Compat_Session_Helpers()
    {
        BridgeSessionIdCompat.SetCseShimGate(() => true);
        try
        {
            var connectUrl = BridgeStatusUtilities.BuildBridgeConnectUrl("env_123");
            var sessionUrl = BridgeStatusUtilities.BuildBridgeSessionUrl("cse_123", "env_123");

            Assert.Equal("https://claude.ai/code?bridge=env_123", connectUrl);
            Assert.Equal("https://claude.ai/code/session_123?bridge=env_123", sessionUrl);
        }
        finally
        {
            BridgeSessionIdCompat.ResetCseShimGate();
        }
    }

    [Fact]
    public void GetBridgeStatus_Matches_Ts_State_Priority()
    {
        Assert.Equal(
            new BridgeStatusInfo("Remote Control failed", "error"),
            BridgeStatusUtilities.GetBridgeStatus("boom", connected: true, sessionActive: true, reconnecting: true));
        Assert.Equal(
            new BridgeStatusInfo("Remote Control reconnecting", "warning"),
            BridgeStatusUtilities.GetBridgeStatus(null, connected: true, sessionActive: true, reconnecting: true));
        Assert.Equal(
            new BridgeStatusInfo("Remote Control active", "success"),
            BridgeStatusUtilities.GetBridgeStatus(null, connected: false, sessionActive: true, reconnecting: false));
        Assert.Equal(
            new BridgeStatusInfo("Remote Control connecting…", "warning"),
            BridgeStatusUtilities.GetBridgeStatus(null, connected: false, sessionActive: false, reconnecting: false));
    }

    [Fact]
    public void Footer_And_Osc8_Helpers_Match_Ts_Text()
    {
        Assert.Equal(
            "Code everywhere with the Claude app or https://claude.ai/code?bridge=env_123",
            BridgeStatusUtilities.BuildIdleFooterText("https://claude.ai/code?bridge=env_123"));
        Assert.Equal(
            "Continue coding in the Claude app or https://claude.ai/code/session_123?bridge=env_123",
            BridgeStatusUtilities.BuildActiveFooterText("https://claude.ai/code/session_123?bridge=env_123"));
        Assert.Equal(
            "\u001b]8;;https://claude.ai/code/session_123\u0007open\u001b]8;;\u0007",
            BridgeStatusUtilities.WrapWithOsc8Link("open", "https://claude.ai/code/session_123"));
    }

    [Fact]
    public void Timestamp_Formats_Local_Time_As_HhMmSs()
    {
        var result = BridgeStatusUtilities.Timestamp(new DateTime(2026, 4, 2, 9, 5, 7));

        Assert.Equal("09:05:07", result);
    }
}
