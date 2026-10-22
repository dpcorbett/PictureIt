using CommunityToolkit.Mvvm.Messaging;
using PictureIt.Core.Model;
using PictureIt.Presentation.Services;

namespace PictureIt.Presentation.Tests;

public sealed class BatchRunnerTests
{
    private const string A = @"C:\photos\a.jpg";
    private const string B = @"C:\photos\b.jpg";
    private static readonly TagKey Title = TagKey.Parse("XMP-dc:Title");

    private readonly Harness _h = new();
    private readonly Dictionary<string, ImageMetadata> _current = new()
    {
        [A] = Metadata.Create(A, ("XMP-dc:Title", "Old")),
        [B] = Metadata.Create(B, ("XMP-dc:Title", "Same")),
    };

    [Fact]
    public async Task Writes_only_files_that_change_and_records_history()
    {
        FilesChangedMessage? announced = null;
        _h.Messenger.Register<FilesChangedMessage>(this, (_, m) => announced = m);

        var result = await _h.Runner.RunAsync(EditSet.Create("Retitle", [Edit(A, "Same"), Edit(B, "Same")]), _current);

        result.ShouldNotBeNull();
        _h.Written.ShouldHaveSingleItem().Files.ShouldHaveSingleItem().FilePath.ShouldBe(A);
        var record = _h.History.Records.ShouldHaveSingleItem();
        record.Description.ShouldBe("Retitle");
        record.FilePaths.ShouldBe([A]);
        record.CanUndo.ShouldBeTrue();
        announced.ShouldNotBeNull().FilePaths.ShouldBe([A]);
    }

    [Fact]
    public async Task Says_so_when_nothing_would_change()
    {
        var result = await _h.Runner.RunAsync(EditSet.Create("Retitle", [Edit(B, "Same")]), _current);

        result.ShouldBeNull();
        await _h.Dialogs.Received(1).ShowInfoAsync("Retitle", Arg.Any<string>());
        await _h.Dialogs.DidNotReceive().ConfirmChangesAsync(Arg.Any<ChangePreview>());
    }

    [Fact]
    public async Task Stops_when_the_preview_is_declined()
    {
        _h.Dialogs.ConfirmChangesAsync(Arg.Any<ChangePreview>()).Returns(false);

        (await _h.Runner.RunAsync(EditSet.Create("Retitle", [Edit(A, "New")]), _current)).ShouldBeNull();

        _h.Written.ShouldBeEmpty();
        _h.History.Records.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reports_failures_and_still_records_the_batch()
    {
        _h.Writer.ApplyAsync(Arg.Any<EditSet>(), Arg.Any<IProgress<WriteProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(call => new WriteResult(call.Arg<EditSet>().BatchId, [new FileWriteResult(A, false, "Locked", [])], true));

        await _h.Runner.RunAsync(EditSet.Create("Retitle", [Edit(A, "New")]), _current);

        await _h.Dialogs.Received(1).ShowWriteFailuresAsync(Arg.Any<WriteResult>());
        var record = _h.History.Records.ShouldHaveSingleItem();
        record.FailedCount.ShouldBe(1);
        record.CanUndo.ShouldBeFalse();
    }

    [Fact]
    public async Task Runs_the_before_write_step_only_after_confirming()
    {
        var ran = false;

        await _h.Runner.RunAsync(EditSet.Create("Retitle", [Edit(A, "New")]), _current, _ =>
        {
            _h.Dialogs.ReceivedCalls().ShouldContain(c => c.GetMethodInfo().Name == nameof(IDialogService.ConfirmChangesAsync));
            ran = true;
            return Task.CompletedTask;
        });

        ran.ShouldBeTrue();
    }

    private static FileEdits Edit(string path, string title) => new(path, [MetadataEdit.Set(Title, title)]);
}
