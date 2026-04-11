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
}
