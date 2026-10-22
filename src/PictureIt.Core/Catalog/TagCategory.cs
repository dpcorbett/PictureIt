namespace PictureIt.Core.Catalog;

/// <summary>Sections of the metadata editor, in display order.</summary>
public enum TagCategory
{
    /// <summary>The everyday fields, written through MWG tags so EXIF, IPTC and XMP stay in sync.</summary>
    Common,
    Description,
    Rights,
    Date,
    Location,
    Camera,
    Exposure,
    Image,
    MakerNotes,
    Other,
    File,
}

/// <summary>How a tag's value is edited and validated.</summary>
public enum EditorKind
{
    Text,
    MultilineText,

    /// <summary>ExifTool date/time: <c>YYYY:MM:DD HH:MM:SS</c>, optionally with sub-seconds and a time zone.</summary>
    DateTime,

    /// <summary>A star rating from 0 to 5.</summary>
    Rating,

    /// <summary>A list of items, edited as text separated by <see cref="TagCatalog.ListSeparator"/>.</summary>
    List,
}
