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
        { BackupDeleted: true } => "Backup cleaned up, can't undo",
        { BackedUp: false } => "No backup, can't undo",
        _ => string.Empty,
    };

    public bool CanUndo => Record.CanUndo;

    /// <summary>Accessible name for the undo button.</summary>
    public string UndoLabel => $"Undo {Description}";
}

/// <summary>Past batches, newest first, with undo from their backups.</summary>
public sealed partial class HistoryViewModel : ObservableObject, IRecipient<FilesChangedMessage>
{
    private readonly IHistoryStore _history;
    private readonly IBackupStore _backups;
    private readonly IDialogService _dialogs;
    private readonly IMessenger _messenger;
    private readonly TimeProvider _time;

    public HistoryViewModel(IHistoryStore history, IBackupStore backups, IDialogService dialogs, IMessenger messenger, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(messenger);
        _history = history;
        _backups = backups;
        _dialogs = dialogs;
        _messenger = messenger;
        _time = time;
        messenger.Register(this);
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

    [RelayCommand]
    private async Task UndoAsync(BatchRecordViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var record = item.Record;
        if (!record.CanUndo)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            $"Undo \"{record.Description}\"?",
            $"This puts {item.Summary} back exactly as they were before. Any changes made to them since will be lost too.",
            "Undo");
        if (!confirmed)
        {
            return;
        }

        var manifest = await _backups.GetAsync(record.BatchId);
        if (manifest is null)
        {
            Update(record with { BackupDeleted = true });
            await _dialogs.ShowErrorAsync("Can't undo", "The backup for this batch no longer exists.");
            return;
        }

        try
        {
            await _backups.RestoreAsync(record.BatchId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync("Couldn't undo", $"Some files couldn't be restored: {ex.Message}");
            return;
        }

        Update(record with { RevertedAt = _time.GetUtcNow() });
        _messenger.Send(new FilesChangedMessage([.. manifest.Entries.Select(e => e.OriginalPath)]));
    }

    private void Update(BatchRecord changed)
    {
        _history.Save([.. _history.Load().Select(r => r.BatchId == changed.BatchId ? changed : r)]);
        Refresh();
    }
}
