using PictureIt.Core.Model;

namespace PictureIt.Core.Abstractions;

/// <summary>Reads the full, editable set of metadata tags from files.</summary>
public interface IMetadataReader
{
    /// <summary>Reads one file. Throws if it can't be read.</summary>
    Task<ImageMetadata> ReadAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>Reads many files, reporting failures per file instead of throwing.</summary>
    Task<IReadOnlyList<MetadataReadResult>> ReadManyAsync(
        IEnumerable<string> filePaths,
        CancellationToken cancellationToken = default);
}
