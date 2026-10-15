namespace PictureIt.Core.Model;

/// <summary>
/// The one definition of what an edit does to a tag's values. <see cref="ChangePreview"/> uses it to predict
/// results and writers use it to turn list additions/removals into a plain "set", so the preview always
/// matches what's written. ExifTool's own <c>+=</c>/<c>-=</c> ordering rules are deliberately not relied on.
/// </summary>
public static class ListEdits
{
    /// <summary>Applies one edit. Null means the tag is absent (or would be removed).</summary>
    public static IReadOnlyList<string>? Apply(IReadOnlyList<string>? current, MetadataEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var existing = current ?? [];
        IReadOnlyList<string> result = edit.Kind switch
        {
            EditKind.Set => [.. edit.Values.Where(v => v.Length > 0)],
            EditKind.Remove => [],

            // Existing items keep their place; new ones are appended once each.
            EditKind.AddToList => [.. existing, .. edit.Values.Distinct().Where(v => !existing.Contains(v))],
            EditKind.RemoveFromList => [.. existing.Where(v => !edit.Values.Contains(v))],
            _ => throw new ArgumentOutOfRangeException(nameof(edit), edit.Kind, "Unknown edit kind."),
        };

        return result.Count == 0 ? null : result;
    }

    /// <summary>
    /// Collapses every tag that has list additions or removals into a single <see cref="EditKind.Set"/>
    /// (or <see cref="EditKind.Remove"/> when nothing is left), computed from the tag's current values.
    /// Other edits pass through unchanged, in their original order.
    /// </summary>
    public static IReadOnlyList<MetadataEdit> Resolve(
        IReadOnlyList<MetadataEdit> edits,
        Func<TagKey, IReadOnlyList<string>?> currentValues)
    {
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(currentValues);

        var listKeys = edits
            .Where(e => e.Kind is EditKind.AddToList or EditKind.RemoveFromList)
            .Select(e => e.Key)
            .ToHashSet();
        if (listKeys.Count == 0)
        {
            return edits;
        }

        var resolved = new List<MetadataEdit>();
        var emitted = new HashSet<TagKey>();
        foreach (var edit in edits)
        {
            if (!listKeys.Contains(edit.Key))
            {
                resolved.Add(edit);
                continue;
            }

            // Emit the collapsed edit where the tag's first edit was.
            if (!emitted.Add(edit.Key))
            {
                continue;
            }

            var values = currentValues(edit.Key);
            foreach (var sameTag in edits.Where(e => e.Key == edit.Key))
            {
                values = Apply(values, sameTag);
            }

            resolved.Add(values is null ? MetadataEdit.Remove(edit.Key) : MetadataEdit.Set(edit.Key, values));
        }

        return resolved;
    }
}
