using CommunityToolkit.Mvvm.Messaging;
using PictureIt.Core.Abstractions;
using PictureIt.Core.History;
using PictureIt.Core.Organise;

namespace PictureIt.Presentation.Services;

/// <summary>
/// The one way files get renamed, moved or copied, like <see cref="BatchRunner"/> is for metadata. The plan has
/// already been previewed in the tool's dialog, so this runs it with progress, records it in history (so it can be
/// undone), tells open views and reports failures.
/// </summary>
public sealed class FileOperationRunner(
    IFileOrganiser organiser,
    IDialogService dialogs,
    IHistoryStore history,
    IMessenger messenger,
    TimeProvider time)
{
    /// <returns>The result, or null if nothing would change.</returns>
    public async Task<FileOperationResult?> RunAsync(FilePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.ChangeCount == 0)
        {
            await dialogs.ShowInfoAsync(plan.Description, "Nothing would change: the photos already have these names and places.");
            return null;
        }

        var result = await dialogs.RunWithProgressAsync(plan.Description, (progress, cancellationToken) => organiser.ExecuteAsync(plan, progress, cancellationToken));

        if (result.Files.Count > 0)
        {
            var record = new BatchRecord(
                plan.BatchId,
                plan.Description,
                time.GetUtcNow(),
                [.. result.Files.Select(f => f.SourcePath)],
                result.SucceededCount,
                result.FailedCount,
                BackedUp: false)
            {
                Kind = plan.Kind switch
                {
                    FileOperationKind.Rename => BatchKind.Rename,
                    FileOperationKind.Move => BatchKind.Move,
                    _ => BatchKind.Copy,
                },
                Moves = result.Moves,
                CreatedFolders = result.CreatedFolders,
            };
            history.Save([record, .. history.Load()]);
        }

        messenger.Send(new FolderChangedMessage(plan.Kind == FileOperationKind.Copy ? [] : result.Moves));

        if (result.FailedCount > 0)
        {
            await dialogs.ShowWriteFailuresAsync(result.ToWriteResult());
        }

        return result;
    }
}
