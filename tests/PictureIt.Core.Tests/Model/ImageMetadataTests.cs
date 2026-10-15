using PictureIt.Core.Model;

namespace PictureIt.Core.Tests.Model;

public class ImageMetadataTests
{
    [Fact]
    public void Looks_up_tags_case_insensitively()
    {
        var metadata = new ImageMetadata("a.jpg", [Tag("IFD0:Model", "Canon EOS R5")]);

        metadata.GetValue("ifd0:model").ShouldBe("Canon EOS R5");
        metadata.TryGetTag(TagKey.Parse("IFD0:MODEL"), out var tag).ShouldBeTrue();
        tag.Description.ShouldBe("Model");
    }

    [Fact]
    public void Returns_null_for_a_missing_tag()
    {
        new ImageMetadata("a.jpg", []).GetValue("IFD0:Model").ShouldBeNull();
    }

    [Fact]
    public void Joins_list_values_for_display()
    {
        var metadata = new ImageMetadata("a.jpg", [Tag("XMP-dc:Subject", "London", "Big Ben")]);

        metadata.GetValue("XMP-dc:Subject").ShouldBe("London; Big Ben");
    }

    [Fact]
    public void Keeps_the_last_of_duplicate_tags()
    {
        var metadata = new ImageMetadata("a.jpg", [Tag("IFD0:Model", "first"), Tag("IFD0:Model", "second")]);

        metadata.Tags.ShouldHaveSingleItem().DisplayValue.ShouldBe("second");
    }

    private static MetadataTag Tag(string key, params string[] values)
    {
        var tagKey = TagKey.Parse(key);
        return new MetadataTag(tagKey, tagKey.Name, values, RawValue: null, IsList: values.Length > 1, IsWritable: true);
    }
}
