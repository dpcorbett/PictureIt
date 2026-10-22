using PictureIt.Core.History;
using PictureIt.Core.Model;

namespace PictureIt.Core.Search;

/// <summary>A folder whose photos (and its subfolders' photos) are in the catalogue.</summary>
public sealed record CatalogFolder(string Path, DateTimeOffset? ScannedAt, int PhotoCount);

/// <summary>What a scan found.</summary>
/// <param name="Failed">Files that couldn't be read, with why. They're still listed, by file name only.</param>
public sealed record ScanResult(int Added, int Updated, int Removed, int Unchanged, IReadOnlyList<string> Failed)
{
    public int Changed => Added + Updated + Removed;
}

/// <summary>Values in use, with how many photos have each, for filter lists.</summary>
public sealed record FacetValue(string Value, int Count);

public sealed record CatalogFacets(IReadOnlyList<FacetValue> Cameras, IReadOnlyList<FacetValue> Lenses, IReadOnlyList<FacetValue> Keywords)
{
    public static CatalogFacets Empty { get; } = new([], [], []);
}

/// <summary>
/// An index of the photos in chosen folders (and below), for searching across folders without reading every file.
/// Scans are incremental: only new and changed files (by size and modified time) are read.
/// </summary>
public interface IPhotoCatalog
{
    Task<IReadOnlyList<CatalogFolder>> GetFoldersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a folder (without scanning it). Returns the catalogue folder that covers it, which is an existing one
    /// if the folder is already inside the catalogue. Folders inside the new one are merged into it.
    /// </summary>
    Task<string> AddFolderAsync(string folder, CancellationToken cancellationToken = default);

    /// <summary>Forgets a folder and its photos (unless another catalogue folder still covers them).</summary>
    Task RemoveFolderAsync(string folder, CancellationToken cancellationToken = default);

    /// <summary>Brings a catalogue folder up to date: reads new and changed photos and drops deleted ones.</summary>
    Task<ScanResult> ScanAsync(string folder, IProgress<WriteProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-reads these files if they're inside a catalogue folder, or drops them if they're gone. Returns how many
    /// catalogue entries changed.
    /// </summary>
    Task<int> RefreshFilesAsync(IReadOnlyCollection<string> filePaths, CancellationToken cancellationToken = default);

    /// <summary>Follows renamed and moved files.</summary>
    Task<int> ApplyMovesAsync(IReadOnlyList<FileMove> moves, CancellationToken cancellationToken = default);

    /// <param name="query">What to find.</param>
    /// <param name="limit">The most to return (the newest first); null for all.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    Task<IReadOnlyList<CatalogEntry>> SearchAsync(PhotoQuery query, int? limit = null, CancellationToken cancellationToken = default);

    Task<int> CountAsync(PhotoQuery query, CancellationToken cancellationToken = default);

    Task<CatalogFacets> GetFacetsAsync(CancellationToken cancellationToken = default);
}

/// <summary>Watches folders for photos being added, removed, renamed or changed outside the app.</summary>
public interface IFolderWatcherFactory
{
    /// <summary>
    /// Calls <paramref name="changed"/> (on a background thread) with the affected paths, once things have been quiet
    /// for a moment. Dispose the result to stop watching.
    /// </summary>
    IDisposable Watch(string folder, bool includeSubfolders, Action<IReadOnlyList<string>> changed);
}
