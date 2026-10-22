using PictureIt.Core.Abstractions;
using PictureIt.Core.History;

namespace PictureIt.Presentation.Services;

/// <summary>Deletes backups past the retention period and marks their batches as no longer undoable.</summary>
public sealed class BackupCleanup(IBackupStore backups, IHistoryStore history, TimeProvider time)
{
    /// <returns>How many backups were deleted.</returns>
    public async Task<int> RunAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        if (retentionDays <= 0)
        {
            return 0;
        }

        var cutoff = time.GetUtcNow().AddDays(-retentionDays);
        var expired = (await backups.ListAsync(cancellationToken)).Where(m => m.CreatedAt < cutoff).Select(m => m.BatchId).ToHashSet();
        foreach (var batchId in expired)
        {
            await backups.DeleteAsync(batchId, cancellationToken);
        }

        if (expired.Count > 0)
        {
            history.Save([.. history.Load().Select(r => expired.Contains(r.BatchId) ? r with { BackupDeleted = true } : r)]);
        }

        return expired.Count;
    }
}
