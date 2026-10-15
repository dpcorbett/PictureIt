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
            var changes = new List<TagChange>();

            // Apply edits in order so later edits to the same tag see earlier ones, as the writer does.
            var working = new Dictionary<TagKey, IReadOnlyList<string>?>();
            foreach (var edit in file.Edits)
            {
                var before = working.TryGetValue(edit.Key, out var pending) ? pending : CurrentValues(metadata, edit.Key);
                working[edit.Key] = ListEdits.Apply(before, edit);
            }

            foreach (var (key, after) in working)
            {
                var oldValue = Display(CurrentValues(metadata, key));
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

    private static IReadOnlyList<string>? CurrentValues(ImageMetadata? metadata, TagKey key) =>
        metadata is not null && metadata.TryGetTag(key, out var tag) ? tag.Values : null;

    private static string? Display(IReadOnlyList<string>? values) => values is null ? null : TagCatalog.JoinList(values);
}
