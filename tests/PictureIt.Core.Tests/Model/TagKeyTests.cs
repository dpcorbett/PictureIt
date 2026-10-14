using PictureIt.Core.Model;

namespace PictureIt.Core.Tests.Model;

public class TagKeyTests
{
    [Theory]
    [InlineData("EXIF:Model", "EXIF", "Model")]
    [InlineData("XMP-dc:Subject", "XMP-dc", "Subject")]
    [InlineData("XMP:XMP-dc:Subject", "XMP:XMP-dc", "Subject")]
    [InlineData(" IPTC : Keywords ", "IPTC", "Keywords")]
    public void Parse_splits_group_and_name(string input, string group, string name)
    {
        var key = TagKey.Parse(input);

        key.Group.ShouldBe(group);
        key.Name.ShouldBe(name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Model")]
    [InlineData(":Model")]
    [InlineData("EXIF:")]
    [InlineData("EXIF: ")]
    public void TryParse_rejects_malformed_keys(string? input)
    {
        TagKey.TryParse(input, out _).ShouldBeFalse();
    }

    [Fact]
    public void Keys_compare_case_insensitively()
    {
        var a = TagKey.Parse("EXIF:Model");
        var b = TagKey.Parse("exif:MODEL");

        a.ShouldBe(b);
        a.GetHashCode().ShouldBe(b.GetHashCode());
    }

    [Fact]
    public void ToString_round_trips()
    {
        TagKey.Parse("XMP-dc:Subject").ToString().ShouldBe("XMP-dc:Subject");
    }
}
