// TS parity status: focused C# unit coverage for the max_turns_reached decision helper; the actual query-loop wiring still depends on the unported model-backed runtime.
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public class QueryMaxTurnsPolicyTests
{
    [Fact]
    public void Evaluate_Returns_Null_When_Max_Turns_Is_Not_Configured()
    {
        var notification = QueryMaxTurnsPolicy.Evaluate(
            maxTurns: null,
            nextTurnCount: 1);

        Assert.Null(notification);
    }

    [Fact]
    public void Evaluate_Returns_Null_When_Next_Turn_Count_Does_Not_Exceed_Max_Turns()
    {
        var notification = QueryMaxTurnsPolicy.Evaluate(
            maxTurns: 3,
            nextTurnCount: 3);

        Assert.Null(notification);
    }

    [Fact]
    public void Evaluate_Returns_Notification_When_Next_Turn_Count_Exceeds_Max_Turns()
    {
        var notification = QueryMaxTurnsPolicy.Evaluate(
            maxTurns: 3,
            nextTurnCount: 4);

        Assert.NotNull(notification);
        Assert.Equal(3, notification!.MaxTurns);
        Assert.Equal(4, notification.TurnCount);
    }
}
