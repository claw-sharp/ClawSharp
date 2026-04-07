using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgeDisplayTagUtilitiesTests
{
    [Fact]
    public void StripDisplayTags_Removes_Lowercase_Tag_Blocks_But_Falls_Back_To_Original_When_Empty()
    {
        var tagged = "<ide_opened_file>foo</ide_opened_file>\nReal text";
        var onlyTags = "<ide_opened_file>foo</ide_opened_file>";

        Assert.Equal("Real text", BridgeDisplayTagUtilities.StripDisplayTags(tagged));
        Assert.Equal(onlyTags, BridgeDisplayTagUtilities.StripDisplayTags(onlyTags));
    }

    [Fact]
    public void StripDisplayTagsAllowEmpty_Returns_Empty_When_All_Content_Is_Tagged()
    {
        var text = "<session-start-hook>noise</session-start-hook>";

        Assert.Equal(string.Empty, BridgeDisplayTagUtilities.StripDisplayTagsAllowEmpty(text));
    }

    [Fact]
    public void StripDisplayTags_Does_Not_Remove_Uppercase_Or_Doctype_Content()
    {
        Assert.Equal("Fix the <Button> layout", BridgeDisplayTagUtilities.StripDisplayTags("Fix the <Button> layout"));
        Assert.Equal("<!DOCTYPE html>", BridgeDisplayTagUtilities.StripDisplayTags("<!DOCTYPE html>"));
    }

    [Fact]
    public void StripIdeContextTags_Only_Removes_Ide_Tags()
    {
        var text = "<ide_opened_file>a</ide_opened_file>\n<other_tag>b</other_tag>\nHello";

        Assert.Equal("<other_tag>b</other_tag>\nHello", BridgeDisplayTagUtilities.StripIdeContextTags(text));
    }
}
