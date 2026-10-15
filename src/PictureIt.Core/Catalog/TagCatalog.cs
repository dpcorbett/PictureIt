using System.Globalization;
using System.Text.RegularExpressions;
using PictureIt.Core.Model;

namespace PictureIt.Core.Catalog;

/// <summary>A tag that the editor always shows, even when the file doesn't have it yet.</summary>
public sealed record CommonField(TagKey Key, string DisplayName, EditorKind Editor);

/// <summary>
/// Knowledge about tags: which section they belong in, how to edit them and how to validate values.
/// Lookups are by tag name regardless of group (e.g. <c>DateTimeOriginal</c> in ExifIFD, XMP-exif or MWG),
/// with the group deciding only for file-system, computed and maker-note tags.
/// </summary>
public static partial class TagCatalog
{
    public const string ListSeparator = "; ";

    /// <summary>
    /// The everyday fields at the top of the editor. MWG tags write every standard location at once
    /// (e.g. MWG:Keywords updates XMP-dc:Subject and IPTC:Keywords), so these stay consistent across apps.
    /// </summary>
    public static IReadOnlyList<CommonField> CommonFields { get; } =
    [
        new(TagKey.Parse("XMP-dc:Title"), "Title", EditorKind.Text),
        new(TagKey.Parse("MWG:Description"), "Description", EditorKind.MultilineText),
        new(TagKey.Parse("MWG:Keywords"), "Keywords", EditorKind.List),
        new(TagKey.Parse("MWG:Rating"), "Rating", EditorKind.Rating),
        new(TagKey.Parse("MWG:Creator"), "Creator", EditorKind.List),
        new(TagKey.Parse("MWG:Copyright"), "Copyright", EditorKind.Text),
        new(TagKey.Parse("MWG:DateTimeOriginal"), "Date taken", EditorKind.DateTime),
        new(TagKey.Parse("MWG:Location"), "Sublocation", EditorKind.Text),
        new(TagKey.Parse("MWG:City"), "City", EditorKind.Text),
        new(TagKey.Parse("MWG:State"), "State / Province", EditorKind.Text),
        new(TagKey.Parse("MWG:Country"), "Country", EditorKind.Text),
    ];

    private static readonly Dictionary<TagKey, CommonField> CommonByKey = CommonFields.ToDictionary(f => f.Key);

    private static readonly Dictionary<string, TagCategory> CategoryByName = BuildCategories();

    private static readonly HashSet<string> FileGroups = new(StringComparer.OrdinalIgnoreCase) { "File", "System", "ExifTool" };

    // Groups defined by a standard; any other group is a camera maker's own (Canon, Nikon, Sony...).
    private static readonly string[] StandardGroupPrefixes =
    [
        "IFD", "ExifIFD", "SubIFD", "GPS", "InteropIFD", "XMP", "IPTC", "MWG", "Composite", "ICC", "JFIF", "Photoshop",
        "PNG", "JPEG", "MPF", "APP", "Adobe", "QuickTime", "Track", "RIFF", "HEIC", "Meta", "File", "System", "ExifTool",
    ];

    private static readonly HashSet<string> ListNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Keywords", "Subject", "HierarchicalSubject", "SupplementalCategories", "PersonInImage", "Contributor", "By-line",
    };

    private static readonly HashSet<string> MultilineNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Description", "ImageDescription", "Caption-Abstract", "UserComment", "XPComment", "UsageTerms", "Instructions",
        "SpecialInstructions",
    };

    private static readonly HashSet<string> DateTimeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "DateTimeOriginal", "CreateDate", "ModifyDate", "DateTimeDigitized", "MetadataDate", "DateCreated",
    };

    // ExifTool's MWG composite tags and the standard tags each one reads from and writes to.
    private static readonly Dictionary<TagKey, TagKey[]> MwgComponentMap = new()
    {
        [TagKey.Parse("MWG:Description")] = Keys("IFD0:ImageDescription", "IPTC:Caption-Abstract", "XMP-dc:Description"),
        [TagKey.Parse("MWG:Keywords")] = Keys("IPTC:Keywords", "XMP-dc:Subject"),
        [TagKey.Parse("MWG:Creator")] = Keys("IFD0:Artist", "IPTC:By-line", "XMP-dc:Creator"),
        [TagKey.Parse("MWG:Copyright")] = Keys("IFD0:Copyright", "IPTC:CopyrightNotice", "XMP-dc:Rights"),
        [TagKey.Parse("MWG:DateTimeOriginal")] = Keys(
            "ExifIFD:DateTimeOriginal", "ExifIFD:SubSecTimeOriginal", "ExifIFD:OffsetTimeOriginal",
            "IPTC:DateCreated", "IPTC:TimeCreated", "XMP-photoshop:DateCreated"),
        [TagKey.Parse("MWG:Location")] = Keys("IPTC:Sub-location", "XMP-iptcCore:Location", "XMP-iptcExt:LocationShownSublocation"),
        [TagKey.Parse("MWG:City")] = Keys("IPTC:City", "XMP-photoshop:City", "XMP-iptcExt:LocationShownCity"),
        [TagKey.Parse("MWG:State")] = Keys("IPTC:Province-State", "XMP-photoshop:State", "XMP-iptcExt:LocationShownProvinceState"),
        [TagKey.Parse("MWG:Country")] = Keys("IPTC:Country-PrimaryLocationName", "XMP-photoshop:Country", "XMP-iptcExt:LocationShownCountryName"),
        [TagKey.Parse("MWG:Rating")] = Keys("XMP-xmp:Rating"),
    };

    /// <summary>
    /// The standard tags an MWG tag writes to (e.g. MWG:Copyright → IFD0:Copyright, IPTC:CopyrightNotice, XMP-dc:Rights).
    /// Empty for anything else.
    /// </summary>
    public static IReadOnlyList<TagKey> MwgComponents(TagKey key) => MwgComponentMap.GetValueOrDefault(key) ?? [];

    private static TagKey[] Keys(params string[] keys) => [.. keys.Select(TagKey.Parse)];

    /// <summary>Returns the common field definition when <paramref name="key"/> is one of <see cref="CommonFields"/>.</summary>
    public static CommonField? FindCommonField(TagKey key) => CommonByKey.GetValueOrDefault(key);

    public static TagCategory CategoryOf(TagKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (CommonByKey.ContainsKey(key))
        {
            return TagCategory.Common;
        }

        if (FileGroups.Contains(key.Group))
        {
            return TagCategory.File;
        }

        if (!StandardGroupPrefixes.Any(p => key.Group.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            return TagCategory.MakerNotes;
        }

        if (CategoryByName.TryGetValue(key.Name, out var category))
        {
            return category;
        }

        return key.Name.StartsWith("GPS", StringComparison.OrdinalIgnoreCase) ? TagCategory.Location : TagCategory.Other;
    }

    /// <summary>
    /// Whether a tag holds a list. ExifTool reports a one-item list as a plain value, so the file alone can't tell us.
    /// </summary>
    public static bool IsListTag(TagKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (CommonByKey.TryGetValue(key, out var common))
        {
            return common.Editor == EditorKind.List;
        }

        // XMP-dc:Creator is a list; EXIF Artist and IPTC Writer-Editor are not.
        return ListNames.Contains(key.Name)
            || (key.Name.Equals("Creator", StringComparison.OrdinalIgnoreCase) && key.Group.StartsWith("XMP", StringComparison.OrdinalIgnoreCase));
    }

    public static EditorKind EditorFor(TagKey key, bool isList)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (CommonByKey.TryGetValue(key, out var common))
        {
            return common.Editor;
        }

        if (isList || IsListTag(key))
        {
            return EditorKind.List;
        }

        if (key.Name.Equals("Rating", StringComparison.OrdinalIgnoreCase))
        {
            return EditorKind.Rating;
        }

        if (DateTimeNames.Contains(key.Name))
        {
            return EditorKind.DateTime;
        }

        return MultilineNames.Contains(key.Name) ? EditorKind.MultilineText : EditorKind.Text;
    }

    /// <summary>Returns a message describing why <paramref name="value"/> is invalid, or null if it's fine. Empty means "remove".</summary>
    public static string? Validate(EditorKind editor, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0)
        {
            return null;
        }

        return editor switch
        {
            EditorKind.DateTime => IsValidDateTime(value) ? null : "Use the form YYYY:MM:DD HH:MM:SS, optionally followed by a time zone such as +01:00.",
            EditorKind.Rating => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var stars) && stars is >= 0 and <= 5
                ? null
                : "Rating must be a whole number from 0 to 5.",
            _ => null,
        };
    }

    /// <summary>Splits list text as typed in the editor, dropping blanks.</summary>
    public static IReadOnlyList<string> SplitList(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static string JoinList(IEnumerable<string> values) => string.Join(ListSeparator, values);

    private static bool IsValidDateTime(string value)
    {
        var match = DateTimePattern().Match(value.Trim());
        if (!match.Success)
        {
            return false;
        }

        var date = $"{match.Groups["date"].Value} {(match.Groups["time"].Success ? match.Groups["time"].Value : "00:00:00")}";
        return DateTime.TryParseExact(
            date,
            ["yyyy:MM:dd HH:mm:ss", "yyyy:MM:dd HH:mm"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);
    }

    [GeneratedRegex(@"^(?<date>\d{4}:\d{2}:\d{2})(?: (?<time>\d{2}:\d{2}(?::\d{2})?))?(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})?$")]
    private static partial Regex DateTimePattern();

    private static Dictionary<string, TagCategory> BuildCategories()
    {
        var map = new Dictionary<string, TagCategory>(StringComparer.OrdinalIgnoreCase);
        void Add(TagCategory category, params string[] names)
        {
            foreach (var name in names)
            {
                map[name] = category;
            }
        }

        Add(TagCategory.Description, "Title", "Description", "ImageDescription", "Caption-Abstract", "Headline", "ObjectName",
            "UserComment", "XPTitle", "XPComment", "XPSubject", "XPKeywords", "Subject", "Keywords", "HierarchicalSubject",
            "Label", "Rating", "RatingPercent", "SupplementalCategories", "Category", "PersonInImage", "Instructions",
            "SpecialInstructions");
        Add(TagCategory.Rights, "Artist", "Creator", "By-line", "By-lineTitle", "XPAuthor", "Copyright", "CopyrightNotice",
            "Rights", "UsageTerms", "Credit", "Source", "WebStatement", "Marked", "Writer-Editor", "Contributor", "Publisher");
        Add(TagCategory.Date, "DateTimeOriginal", "CreateDate", "ModifyDate", "DateTimeDigitized", "MetadataDate",
            "DateCreated", "TimeCreated", "DigitalCreationDate", "DigitalCreationTime", "OffsetTime", "OffsetTimeOriginal",
            "OffsetTimeDigitized", "SubSecTime", "SubSecTimeOriginal", "SubSecTimeDigitized", "SubSecDateTimeOriginal",
            "SubSecCreateDate", "SubSecModifyDate", "DateTimeCreated", "TimeZone", "TimeZoneOffset");
        Add(TagCategory.Location, "City", "State", "Country", "Location", "Sub-location", "Province-State",
            "Country-PrimaryLocationName", "Country-PrimaryLocationCode", "CountryCode", "LocationCreated", "LocationShown");
        Add(TagCategory.Camera, "Make", "Model", "LensMake", "LensModel", "Lens", "LensInfo", "LensID", "LensSpec",
            "SerialNumber", "BodySerialNumber", "InternalSerialNumber", "LensSerialNumber", "Software", "OwnerName",
            "CameraOwnerName", "FirmwareVersion", "UniqueCameraModel", "HostComputer", "CreatorTool");
        Add(TagCategory.Exposure, "ExposureTime", "FNumber", "ISO", "FocalLength", "FocalLengthIn35mmFormat",
            "FocalLength35efl", "ExposureProgram", "ExposureCompensation", "ExposureMode", "MeteringMode", "Flash",
            "WhiteBalance", "ApertureValue", "ShutterSpeedValue", "MaxApertureValue", "BrightnessValue", "LightSource",
            "SceneCaptureType", "SensitivityType", "RecommendedExposureIndex", "SubjectDistance", "SubjectDistanceRange",
            "DigitalZoomRatio", "Contrast", "Saturation", "Sharpness", "GainControl", "ShutterSpeed", "Aperture",
            "LightValue", "ScaleFactor35efl", "CircleOfConfusion", "FOV", "HyperfocalDistance", "DOF");
        Add(TagCategory.Image, "ImageWidth", "ImageHeight", "ImageSize", "Megapixels", "Orientation", "ColorSpace",
            "BitsPerSample", "ColorComponents", "XResolution", "YResolution", "ResolutionUnit", "YCbCrPositioning",
            "YCbCrSubSampling", "ExifImageWidth", "ExifImageHeight", "Compression", "EncodingProcess", "PhotometricInterpretation",
            "SamplesPerPixel", "ExifVersion", "FlashpixVersion", "ComponentsConfiguration", "JFIFVersion",
            "CustomRendered", "SensingMethod", "FileSource", "SceneType", "ProfileDescription");
        return map;
    }
}
