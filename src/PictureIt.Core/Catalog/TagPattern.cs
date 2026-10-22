using PictureIt.Core.Model;

namespace PictureIt.Core.Catalog;

/// <summary>
/// Expands removal patterns (<c>*:gps*</c>, <c>MakerNotes:all</c>, <c>*:all</c>) into the tags they'd remove,
/// matching how ExifTool interprets them, so previews can list every affected tag.
/// </summary>
public static class TagPattern
{
    /// <summary>Groups that are derived from other tags or from the file; removing the source removes them too.</summary>
    private static readonly HashSet<string> DerivedGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        "Composite", "MWG", "File", "System", "ExifTool",
    };

    /// <summary>The colour profile survives "remove everything"; see <see cref="Matches"/>.</summary>
    public const string ColourProfileGroup = "ICC_Profile";

    public static bool Matches(TagKey pattern, TagKey key)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(key);

        if (DerivedGroups.Contains(key.Group))
        {
            return false;
        }

        var groupMatches = pattern.Group switch
        {
            TagKey.AnyGroup => true,
            _ when pattern.Group.Equals("MakerNotes", StringComparison.OrdinalIgnoreCase) => TagCatalog.CategoryOf(key) == TagCategory.MakerNotes,
            _ => pattern.Group.Equals(key.Group, StringComparison.OrdinalIgnoreCase),
        };
        if (!groupMatches)
        {
            return false;
        }

        // ExifToolWriter keeps the colour profile when removing everything, because without it colours display wrongly.
        if (pattern.Group == TagKey.AnyGroup && IsAll(pattern) && key.Group.StartsWith(ColourProfileGroup, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsAll(pattern) || Glob(pattern.Name, key.Name);
    }

    /// <summary>True for <c>*:all</c>, i.e. remove every tag.</summary>
    public static bool IsRemoveEverything(TagKey pattern) => pattern.Group == TagKey.AnyGroup && IsAll(pattern);

    private static bool IsAll(TagKey pattern) => pattern.Name.Equals("all", StringComparison.OrdinalIgnoreCase);

    /// <summary>Case-insensitive match where <c>*</c> matches any run of characters.</summary>
    private static bool Glob(string pattern, string text)
    {
        var parts = pattern.Split('*');
        if (parts.Length == 1)
        {
            return pattern.Equals(text, StringComparison.OrdinalIgnoreCase);
        }

        if (!text.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var position = parts[0].Length;
        for (var i = 1; i < parts.Length - 1; i++)
        {
            var found = text.IndexOf(parts[i], position, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                return false;
            }

            position = found + parts[i].Length;
        }

        var last = parts[^1];
        return text.Length - position >= last.Length && text.EndsWith(last, StringComparison.OrdinalIgnoreCase);
    }
}
