using PictureIt.Core.Catalog;
using PictureIt.Core.Model;

namespace PictureIt.Core.Tests.Catalog;

public class TagPatternTests
{
    [Theory]
    [InlineData("*:gps*", "GPS:GPSLatitude", true)]
    [InlineData("*:gps*", "XMP-exif:GPSLongitude", true)]
    [InlineData("*:gps*", "IFD0:Model", false)]
    [InlineData("*:*SerialNumber", "ExifIFD:LensSerialNumber", true)]
    [InlineData("*:*SerialNumber", "ExifIFD:SerialNumberFormat", false)]
    [InlineData("*:City", "XMP-photoshop:City", true)]
    [InlineData("*:City", "IPTC:CityCode", false)]
    [InlineData("MakerNotes:all", "Canon:CanonImageType", true)]
    [InlineData("MakerNotes:all", "IFD0:Make", false)]
    [InlineData("GPS:all", "GPS:GPSLatitude", true)]
    [InlineData("*:all", "IFD0:Model", true)]
    [InlineData("*:all", "ICC_Profile:ProfileDescription", false)]
    [InlineData("*:gps*", "Composite:GPSPosition", false)]
    [InlineData("*:all", "File:FileType", false)]
    public void Matches_like_ExifTool(string pattern, string key, bool expected)
    {
        TagPattern.Matches(TagKey.Parse(pattern), TagKey.Parse(key)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("*:gps*", true, "gps*")]
    [InlineData("MakerNotes:all", true, "MakerNotes:all")]
    [InlineData("IFD0:Model", false, "IFD0:Model")]
    public void Knows_patterns_and_their_ExifTool_spelling(string key, bool isPattern, string exifToolName)
    {
        var tagKey = TagKey.Parse(key);

        tagKey.IsPattern.ShouldBe(isPattern);
        tagKey.ToExifToolName().ShouldBe(exifToolName);
    }

    [Fact]
    public void Patterns_can_only_remove()
    {
        Should.Throw<ArgumentException>(() => MetadataEdit.Set(TagKey.Parse("*:gps*"), "x"));
    }
}
