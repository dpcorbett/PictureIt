using PictureIt.Core.Model;

namespace PictureIt.Core.Operations;

[Flags]
public enum MetadataGroups
{
    None = 0,

    /// <summary>Title, description and keywords.</summary>
    Description = 1,

    /// <summary>Creator and copyright.</summary>
    Rights = 2,

    /// <summary>GPS coordinates and place names.</summary>
    Location = 4,

    DateTaken = 8,

    Rating = 16,

    All = Description | Rights | Location | DateTaken | Rating,
}

/// <summary>
/// "Copy from this photo, paste onto those": makes chosen groups of fields on the targets match the source,
/// including removing fields the source doesn't have.
/// </summary>
public static class CopyMetadata
{
    private static readonly (MetadataGroups Group, TagKey Key, bool Raw)[] Fields =
    [
        (MetadataGroups.Description, TagKey.Parse("XMP-dc:Title"), false),
        (MetadataGroups.Description, TagKey.Parse("MWG:Description"), false),
        (MetadataGroups.Description, TagKey.Parse("MWG:Keywords"), false),
        (MetadataGroups.Rights, TagKey.Parse("MWG:Creator"), false),
        (MetadataGroups.Rights, TagKey.Parse("MWG:Copyright"), false),

        // Coordinates are copied in raw (decimal) form; the display form ("51 deg 30' 2.52\"") isn't meant for writing.
        (MetadataGroups.Location, TagKey.Parse("GPS:GPSLatitude"), true),
        (MetadataGroups.Location, TagKey.Parse("GPS:GPSLatitudeRef"), true),
        (MetadataGroups.Location, TagKey.Parse("GPS:GPSLongitude"), true),
        (MetadataGroups.Location, TagKey.Parse("GPS:GPSLongitudeRef"), true),
        (MetadataGroups.Location, TagKey.Parse("GPS:GPSAltitude"), true),
        (MetadataGroups.Location, TagKey.Parse("GPS:GPSAltitudeRef"), true),
        (MetadataGroups.Location, TagKey.Parse("MWG:Location"), false),
        (MetadataGroups.Location, TagKey.Parse("MWG:City"), false),
        (MetadataGroups.Location, TagKey.Parse("MWG:State"), false),
        (MetadataGroups.Location, TagKey.Parse("MWG:Country"), false),
        (MetadataGroups.DateTaken, TagKey.Parse("MWG:DateTimeOriginal"), false),
        (MetadataGroups.Rating, TagKey.Parse("MWG:Rating"), false),
    ];

    public static IReadOnlyList<MetadataEdit> EditsFrom(ImageMetadata source, MetadataGroups groups)
    {
        ArgumentNullException.ThrowIfNull(source);
        var edits = new List<MetadataEdit>();
        foreach (var (group, key, raw) in Fields)
        {
            if (!groups.HasFlag(group))
            {
                continue;
            }

            if (!source.TryGetTag(key, out var tag) || tag.Values.All(v => v.Length == 0))
            {
                edits.Add(MetadataEdit.Remove(key));
            }
            else if (raw)
            {
                edits.Add(MetadataEdit.SetRaw(key, tag.RawValue ?? tag.DisplayValue));
            }
            else
            {
                edits.Add(MetadataEdit.Set(key, tag.Values));
            }
        }

        return edits;
    }

    /// <summary>The groups the source photo actually has something in, to suggest what to paste.</summary>
    public static MetadataGroups GroupsPresentIn(ImageMetadata source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Fields
            .Where(f => source.TryGetTag(f.Key, out var tag) && tag.Values.Any(v => v.Length > 0))
            .Aggregate(MetadataGroups.None, (all, f) => all | f.Group);
    }
}
