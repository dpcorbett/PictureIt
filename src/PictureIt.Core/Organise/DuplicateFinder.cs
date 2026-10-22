namespace PictureIt.Core.Organise;

/// <summary>What a duplicate scan knows about one file.</summary>
/// <param name="FilePath">The file.</param>
/// <param name="Length">Size in bytes.</param>
/// <param name="ContentHash">A hash of the whole file, or null when no other file has the same size (so it can't be identical).</param>
/// <param name="DateTaken">When the photo was taken, if known.</param>
/// <param name="Camera">The camera make and model, if known.</param>
public sealed record PhotoFingerprint(string FilePath, long Length, string? ContentHash, DateTime? DateTaken, string? Camera);

public enum DuplicateKind
{
    /// <summary>Byte-for-byte the same file.</summary>
    Identical,

    /// <summary>Different files taken by the same camera at the same moment: an edited copy, a RAW+JPEG pair, or a burst.</summary>
    SameMoment,
}

public sealed record DuplicateGroup(DuplicateKind Kind, IReadOnlyList<string> FilePaths);

/// <summary>Groups photos that are copies of each other.</summary>
public static class DuplicateFinder
{
    /// <param name="photos">The photos to compare.</param>
    /// <param name="includeSameMoment">Also group different files with the same camera and time taken.</param>
    /// <returns>
    /// Groups of two or more, largest first. With <paramref name="includeSameMoment"/>, files linked either way end up
    /// in one group, which is <see cref="DuplicateKind.Identical"/> only if every file in it is the same.
    /// </returns>
    public static IReadOnlyList<DuplicateGroup> Find(IEnumerable<PhotoFingerprint> photos, bool includeSameMoment)
    {
        ArgumentNullException.ThrowIfNull(photos);
        var list = photos.DistinctBy(p => p.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
        var parent = Enumerable.Range(0, list.Count).ToArray();

        int Root(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        void Link(IEnumerable<int> indexes)
        {
            var first = -1;
            foreach (var i in indexes)
            {
                if (first < 0)
                {
                    first = Root(i);
                }
                else
                {
                    parent[Root(i)] = first;
                }
            }
        }

        var indexed = list.Select((p, i) => (Photo: p, Index: i)).ToList();
        foreach (var same in indexed.Where(x => x.Photo.ContentHash is not null).GroupBy(x => (x.Photo.Length, x.Photo.ContentHash)))
        {
            Link(same.Select(x => x.Index));
        }

        if (includeSameMoment)
        {
            foreach (var same in indexed
                .Where(x => x.Photo.DateTaken is not null && !string.IsNullOrWhiteSpace(x.Photo.Camera))
                .GroupBy(x => (x.Photo.DateTaken, Camera: x.Photo.Camera!.Trim().ToUpperInvariant())))
            {
                Link(same.Select(x => x.Index));
            }
        }

        return [.. indexed
            .GroupBy(x => Root(x.Index))
            .Where(g => g.Count() > 1)
            .Select(g =>
            {
                var members = g.Select(x => x.Photo).ToList();
                var identical = members.All(m => m.ContentHash is not null && m.ContentHash == members[0].ContentHash && m.Length == members[0].Length);
                return new DuplicateGroup(
                    identical ? DuplicateKind.Identical : DuplicateKind.SameMoment,
                    [.. members.Select(m => m.FilePath).Order(StringComparer.CurrentCultureIgnoreCase)]);
            })
            .OrderByDescending(g => g.FilePaths.Count)
            .ThenBy(g => g.FilePaths[0], StringComparer.CurrentCultureIgnoreCase)];
    }
}
