using System.Text.RegularExpressions;
using PictureIt.Core.Catalog;
using PictureIt.Core.Model;

namespace PictureIt.Core.Operations;

public enum FindReplaceScope
{
    /// <summary>Title, description, keywords, creator, copyright and place names.</summary>
    CommonFields,

    /// <summary>Every editable text tag in the file.</summary>
    AllTextTags,
}

public sealed record FindReplaceOptions(string Find, string Replace, bool UseRegex, bool MatchCase, FindReplaceScope Scope)
{
    /// <summary>Why the options can't be used, or null if they're fine.</summary>
    public string? Validate()
    {
        if (string.IsNullOrEmpty(Find))
        {
            return "Enter the text to find.";
        }

        if (UseRegex)
        {
            try
            {
                _ = new Regex(Find, RegexOptions.None, FindReplace.Timeout);
            }
            catch (ArgumentException ex)
            {
                return $"The pattern isn't valid: {ex.Message}";
            }
        }

        return null;
    }
}

/// <summary>Replaces text in tag values. List tags are handled item by item; emptied tags are removed.</summary>
public static class FindReplace
{
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    private static readonly HashSet<string> SkippedGroups = new(StringComparer.OrdinalIgnoreCase) { "MWG", "Composite", "File", "System", "ExifTool" };

    public static IReadOnlyList<MetadataEdit> EditsFor(ImageMetadata metadata, FindReplaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Validate() is { } problem)
        {
            throw new ArgumentException(problem, nameof(options));
        }

        var regex = new Regex(
            options.UseRegex ? options.Find : Regex.Escape(options.Find),
            options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            Timeout);

        // Plain text: "$" in the replacement means a literal dollar sign, not a group reference.
        var replacement = options.UseRegex ? options.Replace : options.Replace.Replace("$", "$$", StringComparison.Ordinal);

        var edits = new List<MetadataEdit>();
        foreach (var tag in Candidates(metadata, options.Scope))
        {
            var replaced = tag.Values
                .Select(v => regex.Replace(v, replacement))
                .Where(v => !tag.IsList || v.Trim().Length > 0)
                .ToList();
            if (replaced.SequenceEqual(tag.Values))
            {
                continue;
            }

            edits.Add(replaced.All(v => v.Length == 0) ? MetadataEdit.Remove(tag.Key) : MetadataEdit.Set(tag.Key, replaced));
        }

        return edits;
    }

    private static IEnumerable<MetadataTag> Candidates(ImageMetadata metadata, FindReplaceScope scope)
    {
        if (scope == FindReplaceScope.CommonFields)
        {
            foreach (var field in TagCatalog.CommonFields.Where(f => f.Editor is EditorKind.Text or EditorKind.MultilineText or EditorKind.List))
            {
                if (metadata.TryGetTag(field.Key, out var tag))
                {
                    yield return tag;
                }
            }

            yield break;
        }

        foreach (var tag in metadata.Tags)
        {
            if (tag.IsWritable
                && !SkippedGroups.Contains(tag.Key.Group)
                && TagCatalog.CategoryOf(tag.Key) != TagCategory.MakerNotes
                && TagCatalog.EditorFor(tag.Key, tag.IsList) is EditorKind.Text or EditorKind.MultilineText or EditorKind.List)
            {
                yield return tag;
            }
        }
    }
}
