using System.Globalization;
using PictureIt.Core.Catalog;
using PictureIt.Core.Model;

namespace PictureIt.Core.Operations;

/// <param name="Offset">How far to move every date (e.g. +1 hour for a camera still on winter time).</param>
/// <param name="TimeZone">If set ("+01:00", "Z"), labels times with this zone without moving the clock.</param>
public sealed record DateShiftOptions(TimeSpan Offset, string? TimeZone = null);

/// <summary>
/// Fixes the dates on photos: shifts every date/time tag by the same amount and, optionally, sets the time zone.
/// IPTC keeps date and time in separate tags, so those are shifted as a pair.
/// </summary>
public static class DateShift
{
    private static readonly TagKey ExifDateTimeOriginal = TagKey.Parse("ExifIFD:DateTimeOriginal");

    private static readonly string[] ExifOffsetTags = ["OffsetTime", "OffsetTimeOriginal", "OffsetTimeDigitized"];

    // IPTC splits each date/time into a date tag and a time tag.
    private static readonly (string Date, string Time)[] IptcPairs = [("DateCreated", "TimeCreated"), ("DigitalCreationDate", "DigitalCreationTime")];

    // Tags derived from others; shifting the sources is enough.
    private static readonly HashSet<string> SkippedGroups = new(StringComparer.OrdinalIgnoreCase) { "MWG", "Composite", "File", "System", "ExifTool" };

    public static IReadOnlyList<MetadataEdit> EditsFor(ImageMetadata metadata, DateShiftOptions options)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(options);
        if (options.TimeZone is { } zone && !ExifDateTime.IsValidZone(zone))
        {
            throw new ArgumentException($"'{zone}' is not a time zone like +01:00.", nameof(options));
        }

        var edits = new List<MetadataEdit>();
        var iptcHandled = new HashSet<TagKey>();

        foreach (var (dateName, timeName) in IptcPairs)
        {
            var dateKey = new TagKey("IPTC", dateName);
            var timeKey = new TagKey("IPTC", timeName);
            if (!metadata.TryGetTag(dateKey, out var dateTag) || !metadata.TryGetTag(timeKey, out var timeTag))
            {
                continue;
            }

            iptcHandled.Add(dateKey);
            iptcHandled.Add(timeKey);
            if (ExifDateTime.TryParse($"{dateTag.DisplayValue} {timeTag.DisplayValue}", out var combined))
            {
                var shifted = Adjust(combined, options);
                AddIfChanged(edits, dateTag, shifted.Value.ToString("yyyy:MM:dd", CultureInfo.InvariantCulture));
                AddIfChanged(edits, timeTag, shifted.ToString()[11..]);
            }
        }

        foreach (var tag in metadata.Tags)
        {
            if (!tag.IsWritable || SkippedGroups.Contains(tag.Key.Group) || iptcHandled.Contains(tag.Key)
                || TagCatalog.EditorFor(tag.Key, tag.IsList) != EditorKind.DateTime
                || !ExifDateTime.TryParse(tag.DisplayValue, out var value))
            {
                continue;
            }

            // EXIF dates have no zone part, so only add one where the format allows it (XMP) or one is already there.
            var allowZone = value.Zone.Length > 0 || tag.Key.Group.StartsWith("XMP", StringComparison.OrdinalIgnoreCase);
            AddIfChanged(edits, tag, Adjust(value, allowZone ? options : options with { TimeZone = null }).ToString());
        }

        // EXIF has no zone in its dates; it keeps the zone in separate OffsetTime tags.
        if (options.TimeZone is { } newZone && newZone != "Z")
        {
            foreach (var name in ExifOffsetTags)
            {
                var key = new TagKey("ExifIFD", name);
                if (metadata.TryGetTag(key, out var offsetTag))
                {
                    AddIfChanged(edits, offsetTag, newZone);
                }
                else if (name == "OffsetTimeOriginal" && metadata.TryGetTag(ExifDateTimeOriginal, out _))
                {
                    edits.Add(MetadataEdit.Set(key, newZone));
                }
            }
        }

        return edits;
    }

    /// <summary>When the photo was taken, from EXIF or (failing that) XMP/IPTC via MWG.</summary>
    public static DateTime? DateTaken(ImageMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        foreach (var key in new[] { ExifDateTimeOriginal, TagKey.Parse("MWG:DateTimeOriginal"), TagKey.Parse("XMP-exif:DateTimeOriginal") })
        {
            if (metadata.GetValue(key) is { } text && ExifDateTime.TryParse(text, out var value) && value.HasTime)
            {
                return value.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// The shift that makes <paramref name="reference"/> show <paramref name="correctTime"/>, for "this photo
    /// was actually taken at…". Null if the reference photo has no date taken.
    /// </summary>
    public static TimeSpan? OffsetFromReference(ImageMetadata reference, DateTime correctTime) =>
        DateTaken(reference) is { } taken ? correctTime - taken : null;

    private static ExifDateTime Adjust(ExifDateTime value, DateShiftOptions options)
    {
        var shifted = value.Shift(options.Offset);
        return options.TimeZone is { } zone ? shifted.WithZone(zone) : shifted;
    }

    private static void AddIfChanged(List<MetadataEdit> edits, MetadataTag tag, string value)
    {
        if (value != tag.DisplayValue)
        {
            edits.Add(MetadataEdit.Set(tag.Key, value));
        }
    }
}
