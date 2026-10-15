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
    private MetadataEdit(TagKey key, EditKind kind, IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(key);
        Key = key;
        Kind = kind;
        Values = values;
    }

    public TagKey Key { get; }

    public EditKind Kind { get; }

    public IReadOnlyList<string> Values { get; }

    public static MetadataEdit Set(TagKey key, string value) => new(key, EditKind.Set, [value]);

    public static MetadataEdit Set(TagKey key, IEnumerable<string> values) => new(key, EditKind.Set, NonEmpty(values));

    public static MetadataEdit Remove(TagKey key) => new(key, EditKind.Remove, []);

    public static MetadataEdit AddToList(TagKey key, params IEnumerable<string> values) =>
        new(key, EditKind.AddToList, NonEmpty(values));

    public static MetadataEdit RemoveFromList(TagKey key, params IEnumerable<string> values) =>
        new(key, EditKind.RemoveFromList, NonEmpty(values));

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
