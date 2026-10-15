namespace PictureIt.Core.Model;

/// <summary>
/// One metadata tag as read from a file.
/// </summary>
/// <param name="Key">The tag's group and name.</param>
/// <param name="Description">Human-readable tag name, e.g. "Camera Model Name".</param>
/// <param name="Values">Formatted value(s). List tags (keywords, subjects) have one entry per item.</param>
/// <param name="RawValue">Unformatted value where it differs from the formatted one (e.g. <c>0.004</c> for "1/250").</param>
/// <param name="IsList">True when the tag holds a list, even if it currently has a single item.</param>
/// <param name="IsWritable">False for computed or file-system values that cannot be edited.</param>
public sealed record MetadataTag(
    TagKey Key,
    string Description,
    IReadOnlyList<string> Values,
    string? RawValue,
    bool IsList,
    bool IsWritable)
{
    public string DisplayValue => string.Join("; ", Values);
}
