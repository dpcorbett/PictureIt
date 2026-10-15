namespace PictureIt.Core.Model;

/// <summary>
/// All metadata read from a single file, keyed by <see cref="TagKey"/>.
/// </summary>
public sealed class ImageMetadata
{
    private readonly Dictionary<TagKey, MetadataTag> _tags;

    public ImageMetadata(string filePath, IEnumerable<MetadataTag> tags)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(tags);
        FilePath = filePath;

        _tags = [];
        foreach (var tag in tags)
        {
            // Last one wins if a reader reports a tag twice.
            _tags[tag.Key] = tag;
        }
    }

    public string FilePath { get; }

    public IReadOnlyCollection<MetadataTag> Tags => _tags.Values;

    public bool TryGetTag(TagKey key, out MetadataTag tag) => _tags.TryGetValue(key, out tag!);

    /// <summary>Returns the formatted value of a tag, or null when the file doesn't have it.</summary>
    public string? GetValue(TagKey key) => _tags.TryGetValue(key, out var tag) ? tag.DisplayValue : null;

    public string? GetValue(string key) => GetValue(TagKey.Parse(key));
}
