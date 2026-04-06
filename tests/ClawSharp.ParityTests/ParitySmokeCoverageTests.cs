using ClawSharp.Tasks;

namespace ClawSharp.ParityTests;

public class ParitySmokeCoverageTests
{
    [Fact]
    public void TaskRegistry_Starts_Empty()
    {
        var registry = new TaskRegistry();

        Assert.Empty(registry.GetAll());
    }
}
