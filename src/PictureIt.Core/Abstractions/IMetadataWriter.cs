using PictureIt.Core.Model;

namespace PictureIt.Core.Abstractions;

/// <summary>
/// Writes an <see cref="EditSet"/> to disk. Implementations back up every file in the batch
/// before changing any of them, and continue past per-file failures, reporting them in the result.
/// </summary>
public interface IMetadataWriter
{
    Task<WriteResult> ApplyAsync(
        EditSet editSet,
        IProgress<WriteProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
