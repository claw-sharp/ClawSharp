using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BoundedUuidSetTests
{
    [Fact]
    public void Add_Stores_New_Uuids_And_Ignores_Duplicates()
    {
        var set = new BoundedUuidSet(3);

        set.Add("a");
        set.Add("a");
        set.Add("b");

        Assert.True(set.Contains("a"));
        Assert.True(set.Contains("b"));
    }

    [Fact]
    public void Add_Evicts_Oldest_Entry_When_Capacity_Is_Reached()
    {
        var set = new BoundedUuidSet(2);

        set.Add("a");
        set.Add("b");
        set.Add("c");

        Assert.False(set.Contains("a"));
        Assert.True(set.Contains("b"));
        Assert.True(set.Contains("c"));
    }

    [Fact]
    public void Clear_Removes_All_Tracked_Uuids_And_Resets_Buffer()
    {
        var set = new BoundedUuidSet(2);
        set.Add("a");
        set.Add("b");

        set.Clear();
        set.Add("c");

        Assert.False(set.Contains("a"));
        Assert.False(set.Contains("b"));
        Assert.True(set.Contains("c"));
    }

    [Fact]
    public void Constructor_Rejects_Non_Positive_Capacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedUuidSet(0));
    }
}
