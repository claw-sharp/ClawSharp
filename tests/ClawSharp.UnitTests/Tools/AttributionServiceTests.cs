using ClawSharp.Core;
using ClawSharp.Tools;

namespace ClawSharp.UnitTests.Tools;

public sealed class AttributionServiceTests
{
    [Fact]
    public void GetAttributionTexts_Uses_ClawSharp_CoAuthor_Default()
    {
        var service = new AttributionService();
        var settings = new ClawSharpSettings
        {
            Runtime = new RuntimeSettings
            {
                Model = "gpt-5.4"
            }
        };

        var result = service.GetAttributionTexts(settings);

        Assert.Equal(
            "Co-authored-by: ClawSharp <clawsharp@oneway8x.com>",
            result.Commit);
        Assert.Equal(
            "🤖 Generated with [ClawSharp](https://github.com/claw-sharp)",
            result.Pr);
    }

    [Fact]
    public void GetAttributionTexts_Can_Disable_Default_CoAuthor_Trailer_Via_Environment()
    {
        var original = Environment.GetEnvironmentVariable("CLAWSHARP_DISABLE_CO_AUTHORED_BY");
        Environment.SetEnvironmentVariable("CLAWSHARP_DISABLE_CO_AUTHORED_BY", "true");

        try
        {
            var service = new AttributionService();
            var result = service.GetAttributionTexts(new ClawSharpSettings());

            Assert.Equal(string.Empty, result.Commit);
            Assert.Equal(
                "🤖 Generated with [ClawSharp](https://github.com/claw-sharp)",
                result.Pr);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_DISABLE_CO_AUTHORED_BY", original);
        }
    }
}
