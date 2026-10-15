namespace PictureIt.Core.Abstractions;

/// <summary>A file copied into a backup, and where it came from.</summary>
public sealed record BackupEntry(string OriginalPath, string BackupPath, long Length);

/// <summary>The backup taken for one batch.</summary>
public sealed record BackupManifest(Guid BatchId, DateTimeOffset CreatedAt, IReadOnlyList<BackupEntry> Entries);

/// <summary>
/// Keeps byte-for-byte copies of files before a batch changes them, so the batch can be undone.
/// </summary>
public interface IBackupStore
{
    Task<BackupManifest> CreateAsync(
        Guid batchId,
        IReadOnlyCollection<string> filePaths,
        CancellationToken cancellationToken = default);

    Task<BackupManifest?> GetAsync(Guid batchId, CancellationToken cancellationToken = default);

    /// <summary>Copies every backed-up file back over its original location.</summary>
    Task RestoreAsync(Guid batchId, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid batchId, CancellationToken cancellationToken = default);
}
