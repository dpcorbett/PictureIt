using PictureIt.Core.Model;

namespace PictureIt.Core.Organise;

public enum FileOperationKind
{
    Rename,
    Move,
    Copy,
}

/// <summary>One file to rename, move or copy.</summary>
/// <param name="SourcePath">Where the file is now.</param>
/// <param name="TargetPath">Where it will be.</param>
/// <param name="Note">Anything worth pointing out, e.g. a value the photo didn't have or a number added to avoid a clash.</param>
public sealed record PlannedFileOperation(string SourcePath, string TargetPath, string? Note)
{
    /// <summary>True when the file stays where it is (a change of case still counts as a change).</summary>
    public bool IsUnchanged => string.Equals(SourcePath, TargetPath, StringComparison.Ordinal);
}

/// <summary>A planned batch of renames, moves or copies, shown to the user before anything happens.</summary>
public sealed record FilePlan(Guid BatchId, FileOperationKind Kind, string Description, IReadOnlyList<PlannedFileOperation> Operations)
{
    public IEnumerable<PlannedFileOperation> Changes => Operations.Where(o => !o.IsUnchanged);

    public int ChangeCount => Operations.Count(o => !o.IsUnchanged);
}

/// <summary>Options for <see cref="FilePlanner.Rename"/>.</summary>
/// <param name="Pattern">The new name, without the extension (which is kept).</param>
/// <param name="OrderByDateTaken">Number photos (<c>{Seq}</c>) in the order they were taken, rather than as given.</param>
/// <param name="FirstNumber">Where <c>{Seq}</c> starts.</param>
public sealed record RenameOptions(NamePattern Pattern, bool OrderByDateTaken = false, int FirstNumber = 1);

/// <summary>Options for <see cref="FilePlanner.Sort"/>.</summary>
/// <param name="FolderPattern">The folder each photo goes in, relative to <paramref name="Destination"/>, e.g. <c>{DateTaken:yyyy}\{DateTaken:MM}</c>.</param>
/// <param name="Destination">The folder to sort into.</param>
/// <param name="Copy">Copy the photos, leaving the originals where they are, rather than moving them.</param>
public sealed record SortOptions(NamePattern FolderPattern, string Destination, bool Copy);

/// <summary>
/// Plans renames and sorts without touching the disk (apart from asking whether files exist). Every target is
/// unique: a clash with another photo in the batch, or with a file that's already there, gets " (2)", " (3)"... added,
/// and nothing is ever overwritten.
/// </summary>
public static class FilePlanner
{
    /// <summary>The highest " (n)" tried before giving up on finding a free name.</summary>
    private const int MaxNumber = 10_000;

    public static FilePlan Rename(
        IReadOnlyList<ImageMetadata> photos,
        RenameOptions options,
        Func<string, bool> fileExists,
        IFormatProvider? culture = null)
    {
        ArgumentNullException.ThrowIfNull(photos);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Pattern.HasFolders)
        {
            throw new ArgumentException("A file name can't contain \\ or /.", nameof(options));
        }

        var ordered = options.OrderByDateTaken
            ? [.. photos.OrderBy(p => PhotoFacts.DateTaken(p) ?? DateTime.MaxValue).ThenBy(p => p.FilePath, StringComparer.CurrentCultureIgnoreCase)]
            : photos;

        var wanted = ordered.Select((photo, index) =>
        {
            var result = options.Pattern.Render(photo, options.FirstNumber + index, culture);
            var folder = Path.GetDirectoryName(photo.FilePath)!;
            if (result.Name.Length == 0)
            {
                return new Wanted(photo.FilePath, photo.FilePath, "The name would be empty, so it's left as it is.");
            }

            var target = Path.Combine(folder, result.Name + Path.GetExtension(photo.FilePath));
            return new Wanted(photo.FilePath, target, MissingNote(result));
        }).ToList();

        var count = photos.Count == 1 ? "1 photo" : $"{photos.Count} photos";
        return Resolve(FileOperationKind.Rename, $"Rename {count} to \"{options.Pattern.Text}\"", wanted, sourcesStay: false, fileExists);
    }

    public static FilePlan Sort(
        IReadOnlyList<ImageMetadata> photos,
        SortOptions options,
        Func<string, bool> fileExists,
        IFormatProvider? culture = null)
    {
        ArgumentNullException.ThrowIfNull(photos);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Destination);

        var wanted = photos.Select((photo, index) =>
        {
            var result = options.FolderPattern.Render(photo, index + 1, culture);
            var target = Path.GetFullPath(Path.Combine(options.Destination, result.Name, Path.GetFileName(photo.FilePath)));

            // Already in the right folder (perhaps with its name in a different case): leave it alone.
            return new Wanted(photo.FilePath, string.Equals(target, photo.FilePath, StringComparison.OrdinalIgnoreCase) ? photo.FilePath : target, MissingNote(result));
        }).ToList();

        var count = photos.Count == 1 ? "1 photo" : $"{photos.Count} photos";
        var kind = options.Copy ? FileOperationKind.Copy : FileOperationKind.Move;
        var description = $"{(options.Copy ? "Copy" : "Move")} {count} into {Path.GetFileName(Path.TrimEndingDirectorySeparator(options.Destination))} by \"{options.FolderPattern.Text}\"";
        return Resolve(kind, description, wanted, sourcesStay: options.Copy, fileExists);
    }

    /// <summary>"name.jpg", or "name (2).jpg" etc. if that's taken.</summary>
    public static string Numbered(string path, int number) =>
        number < 2
            ? path
            : Path.Combine(Path.GetDirectoryName(path)!, $"{Path.GetFileNameWithoutExtension(path)} ({number}){Path.GetExtension(path)}");

    private static FilePlan Resolve(FileOperationKind kind, string description, List<Wanted> wanted, bool sourcesStay, Func<string, bool> fileExists)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;

        // Files that will have moved away by the end free their paths; everything else on disk is in the way.
        var leaving = sourcesStay
            ? new HashSet<string>(comparer)
            : wanted.Where(w => !comparer.Equals(w.Source, w.Target)).Select(w => w.Source).ToHashSet(comparer);
        var staying = sourcesStay
            ? wanted.Select(w => w.Source).ToHashSet(comparer)
            : wanted.Where(w => comparer.Equals(w.Source, w.Target)).Select(w => w.Source).ToHashSet(comparer);
        var taken = new HashSet<string>(staying, comparer);

        var operations = new List<PlannedFileOperation>();
        foreach (var w in wanted)
        {
            // Staying put (or only changing case) never clashes.
            if (!sourcesStay && comparer.Equals(w.Source, w.Target))
            {
                operations.Add(new PlannedFileOperation(w.Source, w.Target, w.Note));
                continue;
            }

            var target = w.Target;
            var number = 1;
            while (taken.Contains(target) || (!leaving.Contains(target) && fileExists(target)))
            {
                if (number >= MaxNumber)
                {
                    throw new InvalidOperationException($"Couldn't find a free name for {Path.GetFileName(w.Target)}.");
                }

                target = Numbered(w.Target, ++number);
            }

            taken.Add(target);
            var note = number > 1
                ? Join(w.Note, $"Added ({number}) because {Path.GetFileName(w.Target)} is taken.")
                : w.Note;
            operations.Add(new PlannedFileOperation(w.Source, target, note));
        }

        return new FilePlan(Guid.NewGuid(), kind, description, operations);
    }

    private static string? MissingNote(NameResult result) =>
        result.Missing.Count == 0
            ? null
            : $"No {string.Join(", ", result.Missing.Select(m => m.Contains(':', StringComparison.Ordinal) ? m : m.ToLowerInvariant()))}.";

    private static string? Join(string? first, string second) => first is null ? second : $"{first} {second}";

    private sealed record Wanted(string Source, string Target, string? Note);
}
