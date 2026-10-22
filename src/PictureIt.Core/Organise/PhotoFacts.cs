using System.Globalization;
using PictureIt.Core.Model;
using PictureIt.Core.Operations;

namespace PictureIt.Core.Organise;

/// <summary>
/// Everyday facts about a photo (date taken, camera, lens, place...), each read from the first of several tags that
/// can hold it, so they work whichever standard the camera or another app used.
/// </summary>
public static class PhotoFacts
{
    private static readonly TagKey[] DateTakenKeys = Keys(
        "MWG:DateTimeOriginal", "ExifIFD:DateTimeOriginal", "XMP-exif:DateTimeOriginal", "XMP-photoshop:DateCreated",
        "ExifIFD:CreateDate", "XMP-xmp:CreateDate", "QuickTime:CreateDate", "Keys:CreationDate");

    public static DateTime? DateTaken(ImageMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        foreach (var key in DateTakenKeys)
        {
            // QuickTime stores "0000:00:00 00:00:00" when the date is unknown, which doesn't parse.
            if (ExifDateTime.TryParse(metadata.GetValue(key), out var date))
            {
                return date.Value;
            }
        }

        return null;
    }

    public static string? Make(ImageMetadata metadata) => First(metadata, "IFD0:Make", "XMP-tiff:Make", "QuickTime:Make");

    public static string? Model(ImageMetadata metadata) => First(metadata, "IFD0:Model", "XMP-tiff:Model", "QuickTime:Model");

    /// <summary>
    /// The model, with the make in front when the model doesn't already include it
    /// ("Canon EOS R5" stays as it is; Sony's "ILCE-7M3" becomes "SONY ILCE-7M3").
    /// </summary>
    public static string? Camera(ImageMetadata metadata)
    {
        var make = Make(metadata);
        var model = Model(metadata);
        if (model is null)
        {
            return make;
        }

        var brand = make?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return brand is null || model.Contains(brand, StringComparison.OrdinalIgnoreCase) ? model : $"{brand} {model}";
    }

    public static string? Lens(ImageMetadata metadata) =>
        First(metadata, "Composite:LensID", "ExifIFD:LensModel", "XMP-aux:Lens", "XMP-exifEX:LensModel", "Composite:Lens");

    public static string? Title(ImageMetadata metadata) => First(metadata, "XMP-dc:Title", "IPTC:ObjectName");

    public static string? Sublocation(ImageMetadata metadata) => First(metadata, "MWG:Location", "XMP-iptcCore:Location", "IPTC:Sub-location");

    public static string? City(ImageMetadata metadata) => First(metadata, "MWG:City", "XMP-photoshop:City", "IPTC:City");

    public static string? State(ImageMetadata metadata) => First(metadata, "MWG:State", "XMP-photoshop:State", "IPTC:Province-State");

    public static string? Country(ImageMetadata metadata) =>
        First(metadata, "MWG:Country", "XMP-photoshop:Country", "IPTC:Country-PrimaryLocationName");

    /// <summary>The first creator, when there are several.</summary>
    public static string? Creator(ImageMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        foreach (var key in Keys("MWG:Creator", "XMP-dc:Creator", "IFD0:Artist", "IPTC:By-line"))
        {
            if (metadata.TryGetTag(key, out var tag) && tag.Values.FirstOrDefault(v => v.Length > 0) is { } creator)
            {
                return creator.Trim();
            }
        }

        return null;
    }

    public static string? Rating(ImageMetadata metadata) => First(metadata, "MWG:Rating", "XMP-xmp:Rating", "IFD0:Rating");

    public static string? Width(ImageMetadata metadata) =>
        First(metadata, "File:ImageWidth", "ExifIFD:ExifImageWidth", "PNG:ImageWidth", "QuickTime:ImageWidth");

    public static string? Height(ImageMetadata metadata) =>
        First(metadata, "File:ImageHeight", "ExifIFD:ExifImageHeight", "PNG:ImageHeight", "QuickTime:ImageHeight");

    public static string? Iso(ImageMetadata metadata) => First(metadata, "ExifIFD:ISO", "XMP-exif:ISO");

    /// <summary>"50mm", from ExifTool's "50.0 mm".</summary>
    public static string? FocalLength(ImageMetadata metadata) => FocalLengthMm(metadata) is { } mm ? $"{Format(mm)}mm" : null;

    /// <summary>"f2.8".</summary>
    public static string? Aperture(ImageMetadata metadata) => FNumber(metadata) is { } f ? $"f{Format(f)}" : null;

    public static double? FocalLengthMm(ImageMetadata metadata) => Number(metadata, "ExifIFD:FocalLength", "XMP-exif:FocalLength");

    public static double? FNumber(ImageMetadata metadata) => Number(metadata, "ExifIFD:FNumber", "XMP-exif:FNumber", "Composite:Aperture");

    /// <summary>Exposure time in seconds (ExifTool's raw value; the display value is "1/250").</summary>
    public static double? ExposureSeconds(ImageMetadata metadata) => Number(metadata, "ExifIFD:ExposureTime", "XMP-exif:ExposureTime");

    public static int? IsoNumber(ImageMetadata metadata) => Number(metadata, "ExifIFD:ISO", "XMP-exif:ISO") is { } iso ? (int)iso : null;

    public static int? RatingNumber(ImageMetadata metadata) =>
        Number(metadata, "MWG:Rating", "XMP-xmp:Rating", "IFD0:Rating") is { } stars ? (int)Math.Clamp(stars, 0, 5) : null;

    public static int? WidthPixels(ImageMetadata metadata) =>
        Number(metadata, "File:ImageWidth", "ExifIFD:ExifImageWidth", "PNG:ImageWidth", "QuickTime:ImageWidth") is { } w ? (int)w : null;

    public static int? HeightPixels(ImageMetadata metadata) =>
        Number(metadata, "File:ImageHeight", "ExifIFD:ExifImageHeight", "PNG:ImageHeight", "QuickTime:ImageHeight") is { } h ? (int)h : null;

    /// <summary>Signed decimal degrees (south and west negative), from ExifTool's composite GPS tags.</summary>
    public static (double Latitude, double Longitude)? Location(ImageMetadata metadata)
    {
        var latitude = SignedNumber(metadata, "Composite:GPSLatitude", "XMP-exif:GPSLatitude");
        var longitude = SignedNumber(metadata, "Composite:GPSLongitude", "XMP-exif:GPSLongitude");
        return latitude is { } lat && longitude is { } lon && Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180 ? (lat, lon) : null;
    }

    public static string? Description(ImageMetadata metadata) =>
        First(metadata, "MWG:Description", "XMP-dc:Description", "IFD0:ImageDescription", "IPTC:Caption-Abstract");

    /// <summary>Keywords from the first place that has any.</summary>
    public static IReadOnlyList<string> Keywords(ImageMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        foreach (var key in Keys("MWG:Keywords", "XMP-dc:Subject", "IPTC:Keywords", "XMP-lr:HierarchicalSubject"))
        {
            if (metadata.TryGetTag(key, out var tag) && tag.Values.Any(v => v.Trim().Length > 0))
            {
                return [.. tag.Values.Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
            }
        }

        return [];
    }

    /// <summary>The formatted value of the first of <paramref name="keys"/> the photo has, trimmed; null if none.</summary>
    public static string? First(ImageMetadata metadata, params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(keys);
        foreach (var key in keys)
        {
            if (metadata.GetValue(key)?.Trim() is { Length: > 0 } value)
            {
                return value;
            }
        }

        return null;
    }

    private static double? Number(ImageMetadata metadata, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!metadata.TryGetTag(TagKey.Parse(key), out var tag))
            {
                continue;
            }

            // The raw value is the plain number; the display value may have units ("50.0 mm").
            var text = tag.RawValue ?? tag.DisplayValue;
            var number = new string([.. text.Trim().TakeWhile(c => char.IsAsciiDigit(c) || c == '.')]);
            if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static double? SignedNumber(ImageMetadata metadata, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (metadata.TryGetTag(TagKey.Parse(key), out var tag)
                && double.TryParse(tag.RawValue ?? tag.DisplayValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static string Format(double value) => Math.Round(value, 1).ToString("0.#", CultureInfo.InvariantCulture);

    private static TagKey[] Keys(params string[] keys) => [.. keys.Select(TagKey.Parse)];
}
