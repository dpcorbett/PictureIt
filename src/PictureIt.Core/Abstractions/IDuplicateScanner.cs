using PictureIt.Core.Model;
using PictureIt.Core.Organise;

namespace PictureIt.Core.Abstractions;

/// <summary>Finds duplicate photos among files on disk.</summary>
public interface IDuplicateScanner
{
    /// <summary>Fingerprints the files (hashing only those that share a size) and groups the duplicates.</summary>
    Task<IReadOnlyList<DuplicateGroup>> ScanAsync(
        IReadOnlyList<string> filePaths,
        bool includeSameMoment,
        IProgress<WriteProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
