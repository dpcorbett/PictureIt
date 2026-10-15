using CommunityToolkit.Mvvm.Messaging;
using PictureIt.Core.Abstractions;
using PictureIt.Core.History;
using PictureIt.Core.Model;

namespace PictureIt.Presentation.Services;

/// <summary>
/// The one way edits get written: preview → confirm → write with progress (and backup) → record in history →
/// tell open views → report failures. Every tool and the editor go through here.
/// </summary>
public sealed class BatchRunner(
    IMetadataWriter writer,
    IDialogService dialogs,
    IHistoryStore history,
    IMessenger messenger,
    TimeProvider time)
{
    /// <param name="editSet">The planned edits.</param>
    /// <param name="currentMetadata">Current metadata of the files, for the preview.</param>
    /// <param name="beforeWrite">Runs after the user confirms and before writing (e.g. making copies to write to).</param>
    /// <returns>The result, or null if nothing would change or the user cancelled.</returns>
    public async Task<WriteResult?> RunAsync(
        EditSet editSet,
        IReadOnlyDictionary<string, ImageMetadata> currentMetadata,
        Func<CancellationToken, Task>? beforeWrite = null)
    {
        ArgumentNullException.ThrowIfNull(editSet);
        var preview = ChangePreview.Create(editSet, currentMetadata);
        if (preview.IsEmpty)
        {
            await dialogs.ShowInfoAsync(editSet.Description, "Nothing would change: the photos already have these values.");
            return null;
        }

        if (!await dialogs.ConfirmChangesAsync(preview))
        {
            return null;
        }

        // Only touch (and back up) files that will actually change.
        var changed = preview.Files.Where(f => f.Changes.Count > 0).Select(f => f.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var toWrite = editSet with { Files = [.. editSet.Files.Where(f => changed.Contains(f.FilePath))] };

        var result = await dialogs.RunWithProgressAsync(editSet.Description, async (progress, cancellationToken) =>
        {
            if (beforeWrite is not null)
            {
                await beforeWrite(cancellationToken);
            }

            return await writer.ApplyAsync(toWrite, progress, cancellationToken);
        });

        var record = new BatchRecord(
            result.BatchId,
            editSet.Description,
            time.GetUtcNow(),
            [.. result.Files.Select(f => f.FilePath)],
            result.SucceededCount,
            result.FailedCount,
            result.BackedUp);
        history.Save([record, .. history.Load()]);

        messenger.Send(new FilesChangedMessage([.. result.Files.Where(f => f.Succeeded).Select(f => f.FilePath)]));

        if (result.FailedCount > 0)
        {
            await dialogs.ShowWriteFailuresAsync(result);
        }

        return result;
    }
}
