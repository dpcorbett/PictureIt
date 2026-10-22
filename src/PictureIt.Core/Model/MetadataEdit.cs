namespace PictureIt.Core.Model;

public enum EditKind
{
    /// <summary>Replace the tag's value (all items, for a list tag).</summary>
    Set,

    /// <summary>Delete the tag.</summary>
    Remove,

    /// <summary>Append items to a list tag. Existing items keep their place; items already present aren't duplicated.</summary>
    AddToList,

    /// <summary>Remove matching items from a list tag.</summary>
    RemoveFromList,
}

/// <summary>
/// A single planned change to one tag. Edits are data, so they can be previewed,
/// stored in batch history and replayed.
/// </summary>
public sealed record MetadataEdit
{
    private MetadataEdit(TagKey key, EditKind kind, IReadOnlyList<string> values, bool raw = false)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.IsPattern && kind != EditKind.Remove)
        {
            throw new ArgumentException("Tag patterns can only be used to remove tags.", nameof(key));
        }

        Key = key;
        Kind = kind;
        Values = values;
        Raw = raw;
    }

    /// <summary>The tag, or for <see cref="EditKind.Remove"/> a pattern such as <c>*:gps*</c> (see <see cref="TagKey.IsPattern"/>).</summary>
    public TagKey Key { get; }

    public EditKind Kind { get; }

    public IReadOnlyList<string> Values { get; }

    /// <summary>
    /// The value is ExifTool's raw (numeric) form, e.g. <c>51.5007</c> rather than <c>51 deg 30' 2.52"</c>.
    /// It's written without conversion and compared with the tag's <see cref="MetadataTag.RawValue"/>.
    /// </summary>
    public bool Raw { get; }

    public static MetadataEdit Set(TagKey key, string value) => new(key, EditKind.Set, [value]);

    public static MetadataEdit Set(TagKey key, IEnumerable<string> values) => new(key, EditKind.Set, NonEmpty(values));

    /// <summary>Sets a raw (numeric) value; see <see cref="Raw"/>.</summary>
    public static MetadataEdit SetRaw(TagKey key, string value) => new(key, EditKind.Set, [value], raw: true);

    /// <summary>Removes a tag, or every tag matching a pattern such as <c>*:gps*</c> or <c>MakerNotes:all</c>.</summary>
    public static MetadataEdit Remove(TagKey key) => new(key, EditKind.Remove, []);

    public static MetadataEdit AddToList(TagKey key, params IEnumerable<string> values) =>
        new(key, EditKind.AddToList, NonEmpty(values));

    public static MetadataEdit RemoveFromList(TagKey key, params IEnumerable<string> values) =>
        new(key, EditKind.RemoveFromList, NonEmpty(values));

    public bool Equals(MetadataEdit? other) =>
        other is not null && Key == other.Key && Kind == other.Kind && Raw == other.Raw && Values.SequenceEqual(other.Values);

    public override int GetHashCode() => HashCode.Combine(Key, Kind, Raw, Values.Count);

    private static string[] NonEmpty(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var array = values.ToArray();
        if (array.Length == 0)
        {
            throw new ArgumentException("At least one value is required.", nameof(values));
        }

        return array;
    }
}

/// <summary>The edits to apply to one file.</summary>
public sealed record FileEdits(string FilePath, IReadOnlyList<MetadataEdit> Edits);

/// <summary>
/// A batch of edits across one or more files, written (and undone) as a unit.
/// </summary>
public sealed record EditSet(Guid BatchId, string Description, IReadOnlyList<FileEdits> Files)
{
    public static EditSet Create(string description, IEnumerable<FileEdits> files) =>
        new(Guid.NewGuid(), description, files.ToArray());
}
