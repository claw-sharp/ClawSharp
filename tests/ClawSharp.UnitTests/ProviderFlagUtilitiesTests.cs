using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class ProviderFlagUtilitiesTests
{
    [Fact]
    public void StripProviderFlags_Removes_Runtime_Provider_Options()
    {
        var stripped = ProviderFlagUtilities.StripProviderFlags(
            ["repl", "--provider", "openai", "--model", "gpt-4o", "--continue"]);

        Assert.Equal(["repl", "--continue"], stripped);
    }

    [Fact]
    public void ParseProviderFlag_Reads_Inline_And_Separate_Values()
    {
        Assert.Equal("openai", ProviderFlagUtilities.ParseProviderFlag(["--provider", "openai"]));
        Assert.Equal("gemini", ProviderFlagUtilities.ParseProviderFlag(["--provider=gemini"]));
        Assert.Null(ProviderFlagUtilities.ParseProviderFlag(["repl"]));
    }
}
