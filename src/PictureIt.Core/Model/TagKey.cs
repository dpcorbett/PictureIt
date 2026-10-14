namespace PictureIt.Core.Model;

/// <summary>
/// Identifies a metadata tag by its group and name, using ExifTool's notation,
/// e.g. <c>EXIF:Model</c>, <c>XMP-dc:Subject</c> or <c>IPTC:Keywords</c>.
/// Comparison is case-insensitive, matching ExifTool's own tag matching.
/// </summary>
public sealed record TagKey
{
    public TagKey(string group, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Group = group.Trim();
        Name = name.Trim();
    }

    public string Group { get; }

    public string Name { get; }

    public static TagKey Parse(string value)
    {
        if (!TryParse(value, out var key))
        {
            throw new FormatException($"'{value}' is not a tag key in the form 'Group:Name'.");
        }

        return key;
    }

    public static bool TryParse(string? value, out TagKey key)
    {
        key = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // Split on the last colon: ExifTool allows several groups ("XMP:XMP-dc:Subject"), and the tag name never contains one.
        var separator = value.LastIndexOf(':');
        if (separator <= 0 || separator == value.Length - 1)
        {
            return false;
        }

        var group = value[..separator];
        var name = value[(separator + 1)..];
        if (string.IsNullOrWhiteSpace(group) || string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        key = new TagKey(group, name);
        return true;
    }

    public bool Equals(TagKey? other) =>
        other is not null
        && StringComparer.OrdinalIgnoreCase.Equals(Group, other.Group)
        && StringComparer.OrdinalIgnoreCase.Equals(Name, other.Name);

    public override int GetHashCode() =>
        HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(Group),
            StringComparer.OrdinalIgnoreCase.GetHashCode(Name));

    public override string ToString() => $"{Group}:{Name}";
}
