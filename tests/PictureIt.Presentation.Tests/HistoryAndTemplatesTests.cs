using CommunityToolkit.Mvvm.Messaging;
using PictureIt.Core.Abstractions;
using PictureIt.Core.History;
using PictureIt.Core.Model;
using PictureIt.Core.Templates;
using PictureIt.Presentation.Services;
using PictureIt.Presentation.ViewModels;

namespace PictureIt.Presentation.Tests;

public sealed class HistoryAndTemplatesTests
{
    private readonly Harness _h = new();

    [Fact]
    public async Task Undo_restores_the_backup_marks_the_batch_and_announces_the_files()
    {
        var record = Record("Shift dates");
        _h.History.Records = [record];
        _h.Backups.GetAsync(record.BatchId, Arg.Any<CancellationToken>())
            .Returns(new BackupManifest(record.BatchId, DateTimeOffset.UtcNow, [new BackupEntry(@"C:\a.jpg", @"C:\backup\a.jpg", 1)]));
        FilesChangedMessage? announced = null;
        _h.Messenger.Register<FilesChangedMessage>(this, (_, m) => announced = m);
        var history = _h.CreateHistory();

        await history.UndoCommand.ExecuteAsync(history.Records[0]);

        await _h.Backups.Received(1).RestoreAsync(record.BatchId, Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
        _h.History.Records.Single().RevertedAt.ShouldNotBeNull();
        history.Records[0].CanUndo.ShouldBeFalse();
        history.Records[0].Status.ShouldStartWith("Undone");
        announced.ShouldNotBeNull().FilePaths.ShouldBe([@"C:\a.jpg"]);
    }

    [Fact]
    public async Task Undo_reports_a_missing_backup()
    {
        _h.History.Records = [Record("Gone")];
        var history = _h.CreateHistory();

        await history.UndoCommand.ExecuteAsync(history.Records[0]);

        await _h.Dialogs.Received(1).ShowErrorAsync("Can't undo", Arg.Any<string>());
        _h.History.Records.Single().BackupDeleted.ShouldBeTrue();
    }

    [Fact]
    public async Task Undo_does_nothing_unless_confirmed()
    {
        _h.Dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        _h.History.Records = [Record("Keep")];
        var history = _h.CreateHistory();

        await history.UndoCommand.ExecuteAsync(history.Records[0]);

        await _h.Backups.DidNotReceive().RestoreAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Summarises_partial_batches()
    {
        new BatchRecordViewModel(Record("x") with { SucceededCount = 8, FailedCount = 2 }).Summary.ShouldBe("8 of 10 photos (2 failed)");
        new BatchRecordViewModel(Record("x")).Summary.ShouldBe("1 photo");
    }

    [Fact]
    public async Task Cleanup_deletes_old_backups_and_marks_their_batches()
    {
        var old = Record("Old");
        var recent = Record("Recent");
        _h.History.Records = [recent, old];
        _h.Backups.ListAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new BackupManifest(old.BatchId, DateTimeOffset.UtcNow.AddDays(-40), []),
            new BackupManifest(recent.BatchId, DateTimeOffset.UtcNow.AddDays(-2), []),
        ]);

        var deleted = await new BackupCleanup(_h.Backups, _h.History, _h.Time).RunAsync(retentionDays: 30, TestContext.Current.CancellationToken);

        deleted.ShouldBe(1);
        await _h.Backups.Received(1).DeleteAsync(old.BatchId, Arg.Any<CancellationToken>());
        _h.History.Records.Single(r => r.BatchId == old.BatchId).CanUndo.ShouldBeFalse();
        _h.History.Records.Single(r => r.BatchId == recent.BatchId).CanUndo.ShouldBeTrue();
    }

    [Fact]
    public async Task Cleanup_keeps_everything_when_retention_is_forever()
    {
        (await new BackupCleanup(_h.Backups, _h.History, _h.Time).RunAsync(retentionDays: 0, TestContext.Current.CancellationToken)).ShouldBe(0);

        await _h.Backups.DidNotReceive().ListAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Templates_can_be_renamed_deleted_exported_and_imported()
    {
        var rights = new MetadataTemplate("Rights", [new TemplateField(TagKey.Parse("MWG:Copyright"), false, ["© Dave"])]);
        _h.Templates.Templates = [rights];
        var templates = new TemplatesViewModel(_h.Templates, _h.Dialogs, _h.FilePicker);

        templates.Templates[0].Summary.ShouldBe("Copyright: © Dave");

        _h.FilePicker.PickFileToSaveAsync("Rights", Arg.Any<string>(), ".json").Returns(@"C:\shared\rights.json");
        await templates.ExportCommand.ExecuteAsync(templates.Templates[0]);

        _h.FilePicker.PickFileToOpenAsync(Arg.Is<IReadOnlyList<string>>(e => e.Contains(".json"))).Returns(@"C:\shared\rights.json");
        await templates.ImportCommand.ExecuteAsync(null);
        templates.Templates.Select(t => t.Name).ShouldBe(["Rights", "Rights (2)"]);

        _h.Dialogs.PromptAsync(Arg.Any<string>(), Arg.Any<string>(), "Rights (2)").Returns("Rights for clients");
        await templates.RenameCommand.ExecuteAsync(templates.Templates[1]);
        await templates.DeleteCommand.ExecuteAsync(templates.Templates[0]);

        _h.Templates.Templates.ShouldHaveSingleItem().Name.ShouldBe("Rights for clients");
    }

    [Fact]
    public async Task Importing_a_bad_file_shows_an_error()
    {
        _h.FilePicker.PickFileToOpenAsync(Arg.Is<IReadOnlyList<string>>(e => e.Contains(".json"))).Returns(@"C:\shared\not-a-template.json");
        var templates = new TemplatesViewModel(_h.Templates, _h.Dialogs, _h.FilePicker);

        await templates.ImportCommand.ExecuteAsync(null);

        await _h.Dialogs.Received(1).ShowErrorAsync("Couldn't import", Arg.Any<string>());
    }

    [Fact]
    public async Task Undoing_a_rename_moves_the_files_back_and_tells_the_library()
    {
        var record = new BatchRecord(Guid.NewGuid(), "Rename", DateTimeOffset.UtcNow, [@"C:\a.jpg"], 1, 0, false)
        {
            Kind = BatchKind.Rename,
            Moves = [new FileMove(@"C:\a.jpg", @"C:\b.jpg")],
        };
        _h.History.Records = [record];
        _h.Organiser.UndoAsync(Arg.Any<BatchRecord>(), Arg.Any<CancellationToken>())
            .Returns(new FileOperationResult(record.BatchId, [new FileOperationOutcome(@"C:\b.jpg", @"C:\a.jpg", true, null)], []));
        FolderChangedMessage? announced = null;
        _h.Messenger.Register<FolderChangedMessage>(this, (_, m) => announced = m);
        var history = _h.CreateHistory();

        history.Records[0].CanUndo.ShouldBeTrue();
        await history.UndoCommand.ExecuteAsync(history.Records[0]);

        await _h.Organiser.Received(1).UndoAsync(Arg.Is<BatchRecord>(r => r.BatchId == record.BatchId), Arg.Any<CancellationToken>());
        await _h.Dialogs.Received(1).ConfirmAsync(Arg.Any<string>(), Arg.Is<string>(m => m.Contains("old names", StringComparison.Ordinal)), "Undo");
        _h.History.Records.Single().RevertedAt.ShouldNotBeNull();
        announced.ShouldNotBeNull().Moves.ShouldBe([new FileMove(@"C:\b.jpg", @"C:\a.jpg")]);
    }

    [Fact]
    public async Task Undoing_an_edit_restores_photos_where_they_are_now()
    {
        var edit = Record("Shift dates") with { CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10) };
        var rename = new BatchRecord(Guid.NewGuid(), "Rename", DateTimeOffset.UtcNow.AddMinutes(-5), [@"C:\a.jpg"], 1, 0, false)
        {
            Kind = BatchKind.Rename,
            Moves = [new FileMove(@"C:\a.jpg", @"C:\renamed.jpg")],
        };
        _h.History.Records = [rename, edit];
        _h.Backups.GetAsync(edit.BatchId, Arg.Any<CancellationToken>())
            .Returns(new BackupManifest(edit.BatchId, edit.CreatedAt, [new BackupEntry(@"C:\a.jpg", @"C:\backup\a.jpg", 1)]));
        var history = _h.CreateHistory();

        await history.UndoCommand.ExecuteAsync(history.Records[1]);

        await _h.Backups.Received(1).RestoreAsync(
            edit.BatchId,
            Arg.Is<IReadOnlyDictionary<string, string>>(d => d[@"C:\a.jpg"] == @"C:\renamed.jpg"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Recycled_batches_point_to_the_Recycle_Bin()
    {
        _h.History.Records = [new BatchRecord(Guid.NewGuid(), "Recycle", DateTimeOffset.UtcNow, [@"C:\a.jpg"], 1, 0, false) { Kind = BatchKind.Recycle }];

        var row = _h.CreateHistory().Records.Single();

        row.CanUndo.ShouldBeFalse();
        row.Status.ShouldContain("Recycle Bin");
    }

    private static BatchRecord Record(string description) =>
        new(Guid.NewGuid(), description, DateTimeOffset.UtcNow, [@"C:\a.jpg"], 1, 0, true);
}
