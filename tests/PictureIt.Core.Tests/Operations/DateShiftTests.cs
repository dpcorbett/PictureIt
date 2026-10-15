using PictureIt.Core.Model;
using PictureIt.Core.Operations;

namespace PictureIt.Core.Tests.Operations;

public class DateShiftTests
{
    private static readonly ImageMetadata Photo = TestMetadata.Create(
        ("ExifIFD:DateTimeOriginal", "2024:06:15 14:32:10"),
        ("ExifIFD:CreateDate", "2024:06:15 14:32:10"),
        ("ExifIFD:OffsetTimeOriginal", "+01:00"),
        ("XMP-xmp:CreateDate", "2024:06:15 14:32:10+01:00"),
        ("IPTC:DateCreated", "2024:06:15"),
        ("IPTC:TimeCreated", "14:32:10+01:00"),
        ("MWG:DateTimeOriginal", "2024:06:15 14:32:10+01:00"),
        ("Composite:SubSecDateTimeOriginal", "2024:06:15 14:32:10+01:00"),
        ("File:FileModifyDate", "2026:10:15 10:00:00+10:30"),
        ("IFD0:Model", "Canon EOS R5"));

    [Fact]
    public void Shifts_every_date_tag_but_not_derived_or_file_dates()
    {
        var edits = DateShift.EditsFor(Photo, new DateShiftOptions(TimeSpan.FromHours(10)));

        edits.ShouldBe(
        [
            MetadataEdit.Set(TagKey.Parse("IPTC:DateCreated"), "2024:06:16"),
            MetadataEdit.Set(TagKey.Parse("IPTC:TimeCreated"), "00:32:10+01:00"),
            MetadataEdit.Set(TagKey.Parse("ExifIFD:DateTimeOriginal"), "2024:06:16 00:32:10"),
            MetadataEdit.Set(TagKey.Parse("ExifIFD:CreateDate"), "2024:06:16 00:32:10"),
            MetadataEdit.Set(TagKey.Parse("XMP-xmp:CreateDate"), "2024:06:16 00:32:10+01:00"),
        ], ignoreOrder: true);
    }

    [Fact]
    public void Setting_a_zone_updates_zone_suffixes_and_EXIF_offset_tags()
    {
        var edits = DateShift.EditsFor(Photo, new DateShiftOptions(TimeSpan.Zero, "+10:00"));

        edits.ShouldBe(
        [
            MetadataEdit.Set(TagKey.Parse("IPTC:TimeCreated"), "14:32:10+10:00"),
            MetadataEdit.Set(TagKey.Parse("XMP-xmp:CreateDate"), "2024:06:15 14:32:10+10:00"),
            MetadataEdit.Set(TagKey.Parse("ExifIFD:OffsetTimeOriginal"), "+10:00"),
        ], ignoreOrder: true);
    }

    [Fact]
    public void Adds_the_EXIF_offset_when_the_photo_has_none()
    {
        var photo = TestMetadata.Create(("ExifIFD:DateTimeOriginal", "2024:06:15 14:32:10"));

        DateShift.EditsFor(photo, new DateShiftOptions(TimeSpan.Zero, "-05:00"))
            .ShouldBe([MetadataEdit.Set(TagKey.Parse("ExifIFD:OffsetTimeOriginal"), "-05:00")]);
    }

    [Fact]
    public void Works_out_the_offset_from_a_reference_photo()
    {
        DateShift.OffsetFromReference(Photo, new DateTime(2024, 6, 15, 15, 32, 10)).ShouldBe(TimeSpan.FromHours(1));
        DateShift.OffsetFromReference(TestMetadata.Create(("IFD0:Model", "x")), DateTime.Now).ShouldBeNull();
    }

    [Fact]
    public void Rejects_an_invalid_zone()
    {
        Should.Throw<ArgumentException>(() => DateShift.EditsFor(Photo, new DateShiftOptions(TimeSpan.Zero, "CET")));
    }
}
