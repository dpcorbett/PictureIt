using PictureIt.Core.Catalog;
using PictureIt.Core.Model;

namespace PictureIt.Core.Tests.Catalog;

public class TagCatalogTests
{
    [Theory]
    [InlineData("MWG:Keywords", TagCategory.Common)]
    [InlineData("XMP-dc:Title", TagCategory.Common)]
    [InlineData("IFD0:Model", TagCategory.Camera)]
    [InlineData("ExifIFD:LensModel", TagCategory.Camera)]
    [InlineData("ExifIFD:ExposureTime", TagCategory.Exposure)]
    [InlineData("ExifIFD:DateTimeOriginal", TagCategory.Date)]
    [InlineData("XMP-exif:DateTimeOriginal", TagCategory.Date)]
    [InlineData("GPS:GPSLatitude", TagCategory.Location)]
    [InlineData("Composite:GPSPosition", TagCategory.Location)]
    [InlineData("XMP-dc:Subject", TagCategory.Description)]
    [InlineData("IPTC:Keywords", TagCategory.Description)]
    [InlineData("IFD0:Copyright", TagCategory.Rights)]
    [InlineData("File:ImageWidth", TagCategory.File)]
    [InlineData("System:FileSize", TagCategory.File)]
    [InlineData("Canon:CanonImageType", TagCategory.MakerNotes)]
    [InlineData("IFD0:SomethingUnusual", TagCategory.Other)]
    public void Categorises_tags(string key, TagCategory expected)
    {
        TagCatalog.CategoryOf(TagKey.Parse(key)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("XMP-dc:Subject", true)]
    [InlineData("IPTC:Keywords", true)]
    [InlineData("MWG:Keywords", true)]
    [InlineData("MWG:Creator", true)]
    [InlineData("XMP-dc:Creator", true)]
    [InlineData("IFD0:Artist", false)]
    [InlineData("XMP-dc:Title", false)]
    public void Knows_which_tags_are_lists(string key, bool expected)
    {
        TagCatalog.IsListTag(TagKey.Parse(key)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("MWG:Description", EditorKind.MultilineText)]
    [InlineData("MWG:Rating", EditorKind.Rating)]
    [InlineData("XMP-xmp:Rating", EditorKind.Rating)]
    [InlineData("ExifIFD:CreateDate", EditorKind.DateTime)]
    [InlineData("IPTC:Keywords", EditorKind.List)]
    [InlineData("IFD0:Model", EditorKind.Text)]
    public void Chooses_an_editor(string key, EditorKind expected)
    {
        TagCatalog.EditorFor(TagKey.Parse(key), isList: false).ShouldBe(expected);
    }

    [Theory]
    [InlineData("2024:06:15 14:32:10")]
    [InlineData("2024:06:15 14:32")]
    [InlineData("2024:06:15")]
    [InlineData("2024:06:15 14:32:10+01:00")]
    [InlineData("2024:06:15 14:32:10.123-05:00")]
    [InlineData("2024:06:15 14:32:10Z")]
    [InlineData("")]
    public void Accepts_valid_dates(string value)
    {
        TagCatalog.Validate(EditorKind.DateTime, value).ShouldBeNull();
    }

    [Theory]
    [InlineData("2024-06-15 14:32:10")]
    [InlineData("2024:13:01 00:00:00")]
    [InlineData("2024:02:30 00:00:00")]
    [InlineData("15/06/2024")]
    [InlineData("2024:06:15 25:00:00")]
    public void Rejects_invalid_dates(string value)
    {
        TagCatalog.Validate(EditorKind.DateTime, value).ShouldNotBeNull();
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("5", true)]
    [InlineData("6", false)]
    [InlineData("-1", false)]
    [InlineData("3.5", false)]
    [InlineData("three", false)]
    public void Validates_ratings(string value, bool valid)
    {
        (TagCatalog.Validate(EditorKind.Rating, value) is null).ShouldBe(valid);
    }

    [Fact]
    public void Splits_and_joins_lists()
    {
        TagCatalog.SplitList(" London ;Big Ben;; ").ShouldBe(["London", "Big Ben"]);
        TagCatalog.JoinList(["London", "Big Ben"]).ShouldBe("London; Big Ben");
    }
}
