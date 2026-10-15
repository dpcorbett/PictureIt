using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using PictureIt.Core.Abstractions;
using PictureIt.Core.History;
using PictureIt.Presentation.Services;

namespace PictureIt.Presentation.ViewModels;

/// <summary>One row on the History page.</summary>
public sealed class BatchRecordViewModel(BatchRecord record)
{
    public BatchRecord Record { get; } = record;

    public string Description => Record.Description;

    public string When => Record.CreatedAt.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture);

    public string Summary => (Record.SucceededCount + Record.FailedCount, Record.FailedCount) switch
    {
        (1, 0) => "1 photo",
        (var total, 0) => $"{total} photos",
        (var total, var failed) => $"{total - failed} of {total} photos ({failed} failed)",
    };

    public string Status => Record switch
    {
        { RevertedAt: { } reverted } => $"Undone {reverted.ToLocalTime().ToString("d MMM, HH:mm", CultureInfo.CurrentCulture)}",
        { Kind: BatchKind.Recycle } => "Restore them from the Recycle Bin if needed",
        { Kind: not BatchKind.Metadata } => string.Empty,
        { BackupDeleted: true } => "Backup cleaned up, can't undo",
        { BackedUp: false } => "No backup, can't undo",
        _ => string.Empty,
    };

    public bool CanUndo => Record.CanUndo;

    /// <summary>Accessible name for the undo button.</summary>
    public string UndoLabel => $"Undo {Description}";
}

/// <summary>Past batches, newest first, with undo from their backups.</summary>
public sealed partial class HistoryViewModel : ObservableObject, IRecipient<FilesChangedMessage>, IRecipient<FolderChangedMessage>
{
    private readonly IHistoryStore _history;
    private readonly IBackupStore _backups;
    private readonly IFileOrganiser _files;
    private readonly IDialogService _dialogs;
    private readonly IMessenger _messenger;
    private readonly TimeProvider _time;

    public HistoryViewModel(IHistoryStore history, IBackupStore backups, IFileOrganiser files, IDialogService dialogs, IMessenger messenger, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(messenger);
        _history = history;
        _backups = backups;
        _files = files;
        _dialogs = dialogs;
        _messenger = messenger;
        _time = time;
        messenger.Register<FilesChangedMessage>(this);
        messenger.Register<FolderChangedMessage>(this);
        Refresh();
    }

    public ObservableCollection<BatchRecordViewModel> Records { get; } = [];

    public bool IsEmpty => Records.Count == 0;

    public void Refresh()
    {
        Records.Clear();
        foreach (var record in _history.Load())
        {
            Records.Add(new BatchRecordViewModel(record));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    public void Receive(FilesChangedMessage message) => Refresh();

    public void Receive(FolderChangedMessage message) => Refresh();

    [RelayCommand]
    private async Task UndoAsync(BatchRecordViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var record = item.Record;
        if (!record.CanUndo)
        {
            return;
        }

        var question = record.Kind switch
        {
            BatchKind.Rename => $"This gives {item.Summary} back their old names.",
            BatchKind.Move => $"This moves {item.Summary} back to where they were.",
            BatchKind.Copy => $"This deletes the copies of {item.Summary}. Any changes made to the copies since will be lost.",
            _ => $"This puts {item.Summary} back exactly as they were before. Any changes made to them since will be lost too.",
        };
        if (!await _dialogs.ConfirmAsync($"Undo \"{record.Description}\"?", question, "Undo"))
        {
            return;
        }

        if (record.Kind != BatchKind.Metadata)
        {
            await UndoFilesAsync(record);
            return;
        }

        var manifest = await _backups.GetAsync(record.BatchId);
        if (manifest is null)
        {
            Update(record with { BackupDeleted = true });
            await _dialogs.ShowErrorAsync("Can't undo", "The backup for this batch no longer exists.");
            return;
        }

        // Photos renamed or moved since are restored where they are now.
        var currentPaths = PathHistory.CurrentPaths(manifest.Entries.Select(e => e.OriginalPath), record.CreatedAt, _history.Load());
        try
        {
            await _backups.RestoreAsync(record.BatchId, currentPaths);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync("Couldn't undo", $"Some files couldn't be restored: {ex.Message}");
            return;
        }

        Update(record with { RevertedAt = _time.GetUtcNow() });
        _messenger.Send(new FilesChangedMessage([.. currentPaths.Values]));
    }

    private async Task UndoFilesAsync(BatchRecord record)
    {
        FileOperationResult result;
        try
        {
            result = await _files.UndoAsync(record);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync("Couldn't undo", ex.Message);
            return;
        }

        Update(record with { RevertedAt = _time.GetUtcNow() });
        _messenger.Send(new FolderChangedMessage(record.MovesFiles ? result.Moves : []));
        if (result.FailedCount > 0)
        {
            await _dialogs.ShowWriteFailuresAsync(result.ToWriteResult());
        }
    }

    private void Update(BatchRecord changed)
    {
        _history.Save([.. _history.Load().Select(r => r.BatchId == changed.BatchId ? changed : r)]);
        Refresh();
    }
}
