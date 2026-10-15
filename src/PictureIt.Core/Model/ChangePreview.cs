using PictureIt.Core.Catalog;

namespace PictureIt.Core.Model;

/// <summary>One tag's value before and after a batch. A null value means the tag is absent.</summary>
public sealed record TagChange(TagKey Key, string? OldValue, string? NewValue);

/// <summary>The changes a batch will make to one file.</summary>
public sealed record FileChangePreview(string FilePath, IReadOnlyList<TagChange> Changes);

/// <summary>
/// What an <see cref="EditSet"/> will do, worked out against the files' current metadata, so it can be shown
/// before anything is written. Edits that wouldn't change anything are left out.
/// </summary>
public sealed record ChangePreview(EditSet EditSet, IReadOnlyList<FileChangePreview> Files)
{
    public int ChangeCount => Files.Sum(f => f.Changes.Count);

    /// <summary>Files that would actually change.</summary>
    public int ChangedFileCount => Files.Count(f => f.Changes.Count > 0);

    public bool IsEmpty => ChangeCount == 0;

    /// <param name="editSet">The planned edits.</param>
    /// <param name="currentMetadata">Current metadata by file path; files missing from it are treated as having no tags.</param>
    public static ChangePreview Create(EditSet editSet, IReadOnlyDictionary<string, ImageMetadata> currentMetadata)
    {
        ArgumentNullException.ThrowIfNull(editSet);
        ArgumentNullException.ThrowIfNull(currentMetadata);

        var files = new List<FileChangePreview>(editSet.Files.Count);
        foreach (var file in editSet.Files)
        {
            currentMetadata.TryGetValue(file.FilePath, out var metadata);

            // Apply edits in order so later edits to the same tag see earlier ones, as the writer does.
            var working = new Dictionary<TagKey, IReadOnlyList<string>?>();
            var raw = new Dictionary<TagKey, bool>();
            foreach (var edit in file.Edits)
            {
                if (edit.Key.IsPattern)
                {
                    foreach (var tag in metadata?.Tags.Where(t => t.IsWritable && TagPattern.Matches(edit.Key, t.Key)) ?? [])
                    {
                        working[tag.Key] = null;
                    }

                    continue;
                }

                var before = working.TryGetValue(edit.Key, out var pending) ? pending : CurrentValues(metadata, edit.Key, edit.Raw);
                working[edit.Key] = ListEdits.Apply(before, edit);
                raw[edit.Key] = edit.Raw;
            }

            var changes = new List<TagChange>();
            foreach (var (key, after) in working)
            {
                var oldValue = Display(CurrentValues(metadata, key, raw.GetValueOrDefault(key)));
                var newValue = Display(after);
                if (oldValue != newValue)
                {
                    changes.Add(new TagChange(key, oldValue, newValue));
                }
            }

            files.Add(new FileChangePreview(file.FilePath, changes));
        }

        return new ChangePreview(editSet, files);
    }

    private static IReadOnlyList<string>? CurrentValues(ImageMetadata? metadata, TagKey key, bool raw)
    {
        if (metadata is null || !metadata.TryGetTag(key, out var tag))
        {
            return null;
        }

        return raw && tag.RawValue is { } rawValue ? [rawValue] : tag.Values;
    }

    private static string? Display(IReadOnlyList<string>? values) => values is null ? null : TagCatalog.JoinList(values);
}
