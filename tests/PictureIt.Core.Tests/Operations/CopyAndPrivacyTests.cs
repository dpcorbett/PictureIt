using PictureIt.Core.Model;
using PictureIt.Core.Operations;
using PictureIt.Core.Templates;

namespace PictureIt.Core.Tests.Operations;

public class CopyAndPrivacyTests
{
    private static readonly ImageMetadata Source = TestMetadata.Create(
        ("XMP-dc:Title", "Big Ben", null),
        ("MWG:Keywords", "London; Big Ben", null),
        ("MWG:Creator", "Dave", null),
        ("GPS:GPSLatitude", "51 deg 30' 2.52\"", "51.5007"),
        ("GPS:GPSLatitudeRef", "North", "N"),
        ("MWG:City", "London", null),
        ("MWG:Rating", "4", null),
        ("IFD0:Orientation", "Rotate 90 CW", "6"),
        ("ExifIFD:SerialNumber", "012345678", null),
        ("Canon:CanonImageType", "Canon EOS R5", null),
        ("GPS:GPSLongitude", "0 deg 7' 28.56\"", "0.1246"),
        ("XMP-exif:GPSLatitude", "51 deg 30' 2.52\" N", "51.5007"),
        ("ICC_Profile:ProfileDescription", "sRGB", null),
        ("Composite:GPSPosition", "51 deg N, 0 deg W", null));

    [Fact]
    public void Copies_chosen_groups_and_clears_fields_the_source_lacks()
    {
        var edits = CopyMetadata.EditsFrom(Source, MetadataGroups.Description | MetadataGroups.Location);

        edits.ShouldContain(MetadataEdit.Set(TagKey.Parse("XMP-dc:Title"), "Big Ben"));
        edits.ShouldContain(MetadataEdit.Set(TagKey.Parse("MWG:Keywords"), ["London", "Big Ben"]));
        edits.ShouldContain(MetadataEdit.Remove(TagKey.Parse("MWG:Description")));
        edits.ShouldContain(MetadataEdit.SetRaw(TagKey.Parse("GPS:GPSLatitude"), "51.5007"));
        edits.ShouldContain(MetadataEdit.SetRaw(TagKey.Parse("GPS:GPSLatitudeRef"), "N"));
        edits.ShouldNotContain(e => e.Key == TagKey.Parse("MWG:Creator"));
    }

    [Fact]
    public void Knows_which_groups_a_photo_has()
    {
        CopyMetadata.GroupsPresentIn(Source).ShouldBe(MetadataGroups.Description | MetadataGroups.Rights | MetadataGroups.Location | MetadataGroups.Rating);
    }

    [Fact]
    public void Previewing_a_raw_copy_onto_an_identical_photo_shows_no_change()
    {
        // Raw values are compared with raw values, and removing tags the photo doesn't have isn't a change.
        Preview(Source, CopyMetadata.EditsFrom(Source, MetadataGroups.All)).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Removing_GPS_previews_every_GPS_tag_in_every_group()
    {
        var keys = Preview(Source, PrivacyStrip.EditsFor(Source, PrivacyOptions.GpsLocation)).Files[0].Changes.Select(c => c.Key.ToString());

        keys.ShouldBe(["GPS:GPSLatitude", "GPS:GPSLatitudeRef", "GPS:GPSLongitude", "XMP-exif:GPSLatitude"], ignoreOrder: true);
    }

    [Fact]
    public void Removing_camera_identifiers_covers_serials_and_maker_notes()
    {
        var keys = Preview(Source, PrivacyStrip.EditsFor(Source, PrivacyOptions.CameraIdentifiers)).Files[0].Changes.Select(c => c.Key.ToString());

        keys.ShouldBe(["ExifIFD:SerialNumber", "Canon:CanonImageType"], ignoreOrder: true);
    }

    [Fact]
    public void Removing_everything_keeps_orientation_and_the_colour_profile()
    {
        var changes = Preview(Source, PrivacyStrip.EditsFor(Source, PrivacyOptions.Everything)).Files[0].Changes;

        changes.ShouldNotContain(c => c.Key.Name == "Orientation");
        changes.ShouldNotContain(c => c.Key.Group == "ICC_Profile");
        changes.ShouldNotContain(c => c.Key.Group == "Composite" || c.Key.Group == "MWG");
        changes.ShouldContain(c => c.Key.ToString() == "XMP-dc:Title" && c.NewValue == null);
    }

    [Fact]
    public void Template_from_a_photo_takes_common_fields_and_adds_to_lists()
    {
        var template = MetadataTemplate.FromPhoto(" Trip ", Source);

        template.Name.ShouldBe("Trip");
        template.ToEdits().ShouldBe(
        [
            MetadataEdit.Set(TagKey.Parse("XMP-dc:Title"), "Big Ben"),
            MetadataEdit.AddToList(TagKey.Parse("MWG:Keywords"), "London", "Big Ben"),
            MetadataEdit.Set(TagKey.Parse("MWG:Rating"), "4"),
            MetadataEdit.AddToList(TagKey.Parse("MWG:Creator"), "Dave"),
            MetadataEdit.Set(TagKey.Parse("MWG:City"), "London"),
        ]);
    }

    private static ChangePreview Preview(ImageMetadata photo, IReadOnlyList<MetadataEdit> edits) =>
        ChangePreview.Create(
            EditSet.Create("test", [new FileEdits(photo.FilePath, edits)]),
            new Dictionary<string, ImageMetadata> { [photo.FilePath] = photo });
}
