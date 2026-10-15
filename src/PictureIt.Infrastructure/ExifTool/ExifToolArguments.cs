using PictureIt.Core.Model;

namespace PictureIt.Infrastructure.ExifTool;

/// <summary>
/// Turns <see cref="MetadataEdit"/>s into ExifTool assignment arguments.
/// </summary>
internal static class ExifToolArguments
{
    /// <summary>
    /// Builds the tag assignments for one file. ExifTool reads arguments one per line, so a value that has
    /// line breaks, or leading/trailing whitespace, is written to a file in <paramref name="valueFileDirectory"/>
    /// and assigned with <c>-TAG&lt;=FILE</c>, which takes the file's bytes exactly.
    /// </summary>
    public static List<string> ForEdits(IEnumerable<MetadataEdit> edits, string valueFileDirectory)
    {
        ArgumentNullException.ThrowIfNull(edits);
        var args = new List<string>();

        foreach (var edit in edits)
        {
            var tag = edit.Key.ToString();
            switch (edit.Kind)
            {
                case EditKind.Remove:
                    args.Add($"-{tag}=");
                    break;

                case EditKind.Set:
                    // Repeated assignments to a list tag within one command build up the list.
                    args.AddRange(edit.Values.Select(v => Assign(tag, string.Empty, v, valueFileDirectory)));
                    break;

                case EditKind.AddToList:
                    args.AddRange(edit.Values.Select(v => Assign(tag, "+", v, valueFileDirectory)));
                    break;

                case EditKind.RemoveFromList:
                    args.AddRange(edit.Values.Select(v => Assign(tag, "-", v, valueFileDirectory)));
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(edits), edit.Kind, "Unknown edit kind.");
            }
        }

        return args;
    }

    internal static bool NeedsValueFile(string value) =>
        value.AsSpan().IndexOfAny('\r', '\n') >= 0
        || (value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])));

    private static string Assign(string tag, string op, string value, string valueFileDirectory)
    {
        if (!NeedsValueFile(value))
        {
            return $"-{tag}{op}={value}";
        }

        Directory.CreateDirectory(valueFileDirectory);
        var file = Path.Combine(valueFileDirectory, $"{Guid.NewGuid():N}.txt");
        File.WriteAllText(file, value); // UTF-8 without BOM, which is what ExifTool stores.
        return $"-{tag}{op}<={file}";
    }
}
