using PictureIt.Core.Model;

namespace PictureIt.Core.Operations;

[Flags]
public enum PrivacyOptions
{
    None = 0,

    /// <summary>GPS coordinates, wherever they're stored.</summary>
    GpsLocation = 1,

    /// <summary>City, state, country and sublocation names.</summary>
    PlaceNames = 2,

    /// <summary>Serial numbers, owner names and the camera maker's private data (which often includes serials).</summary>
    CameraIdentifiers = 4,

    /// <summary>All metadata, except the orientation and colour profile the photo needs to display correctly.</summary>
    Everything = 8,
}

/// <summary>Builds removals for privacy presets, typically before sharing photos.</summary>
public static class PrivacyStrip
{
    private static readonly string[] PlaceNameTags =
    [
        "City", "State", "Province-State", "Country", "CountryCode", "Country-PrimaryLocationName", "Country-PrimaryLocationCode",
        "Location", "Sub-location",

        // IPTC Extension location structures (XMP-iptcExt:LocationShownCity, LocationCreatedCountryName, ...).
        "LocationShown*", "LocationCreated*",
    ];

    private static readonly string[] IdentifierTags = ["*SerialNumber", "OwnerName", "CameraOwnerName", "ImageUniqueID"];

    private static readonly TagKey Orientation = TagKey.Parse("IFD0:Orientation");

    public static IReadOnlyList<MetadataEdit> EditsFor(ImageMetadata metadata, PrivacyOptions options)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var edits = new List<MetadataEdit>();

        if (options.HasFlag(PrivacyOptions.Everything))
        {
            edits.Add(MetadataEdit.Remove(new TagKey(TagKey.AnyGroup, "all")));

            // Put the orientation back, or portrait photos would display sideways.
            if (metadata.TryGetTag(Orientation, out var orientation) && orientation.RawValue is { } raw)
            {
                edits.Add(MetadataEdit.SetRaw(Orientation, raw));
            }

            return edits;
        }

        if (options.HasFlag(PrivacyOptions.GpsLocation))
        {
            edits.Add(MetadataEdit.Remove(new TagKey(TagKey.AnyGroup, "gps*")));
        }

        if (options.HasFlag(PrivacyOptions.PlaceNames))
        {
            edits.AddRange(PlaceNameTags.Select(name => MetadataEdit.Remove(new TagKey(TagKey.AnyGroup, name))));
        }

        if (options.HasFlag(PrivacyOptions.CameraIdentifiers))
        {
            edits.Add(MetadataEdit.Remove(TagKey.Parse("MakerNotes:all")));
            edits.AddRange(IdentifierTags.Select(name => MetadataEdit.Remove(new TagKey(TagKey.AnyGroup, name))));
        }

        return edits;
    }
}
