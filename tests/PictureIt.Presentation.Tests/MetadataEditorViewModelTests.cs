using PictureIt.Core.Abstractions;
using PictureIt.Core.Catalog;
using PictureIt.Core.Model;
using PictureIt.Presentation.Services;
using PictureIt.Presentation.ViewModels;

namespace PictureIt.Presentation.Tests;

public sealed class MetadataEditorViewModelTests
{
    private readonly IMetadataReader _reader = Substitute.For<IMetadataReader>();
    private readonly IMetadataWriter _writer = Substitute.For<IMetadataWriter>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly InMemorySettingsStore _settings = new();
    private readonly MetadataEditorViewModel _editor;

    public MetadataEditorViewModelTests()
    {
        _reader.ReadAsync(Metadata.Path, Arg.Any<CancellationToken>()).Returns(Metadata.Sample());
        _dialogs.ConfirmChangesAsync(Arg.Any<ChangePreview>()).Returns(true);
        _writer.ApplyAsync(Arg.Any<EditSet>(), Arg.Any<IProgress<WriteProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(call => Succeeded(call.Arg<EditSet>()));
        _editor = new MetadataEditorViewModel(_reader, _writer, _dialogs, _settings);
    }

    [Fact]
    public async Task Shows_common_fields_first_even_when_the_file_lacks_them()
    {
        await _editor.LoadAsync(Metadata.Path);

        var common = _editor.Groups[0];
        common.Category.ShouldBe(TagCategory.Common);
        common.Fields.Select(f => f.Key).ShouldBe(TagCatalog.CommonFields.Select(f => f.Key));
        Field("MWG:Keywords").Value.ShouldBe("London; Big Ben");
        Field("MWG:Copyright").Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Groups_other_tags_by_category_in_display_order()
    {
        await _editor.LoadAsync(Metadata.Path);

        _editor.Groups.Select(g => g.Category).ShouldBe(
            [TagCategory.Common, TagCategory.Location, TagCategory.Camera, TagCategory.Exposure, TagCategory.File]);
    }

    [Fact]
    public async Task Marks_computed_and_file_tags_read_only()
    {
        await _editor.LoadAsync(Metadata.Path);

        Field("File:FileType").IsReadOnly.ShouldBeTrue();
        Field("Composite:GPSPosition").IsReadOnly.ShouldBeTrue();
        Field("IFD0:Model").IsReadOnly.ShouldBeFalse();
    }

    [Fact]
    public async Task Tracks_edits_and_reverts_them()
    {
        await _editor.LoadAsync(Metadata.Path);

        Field("IFD0:Model").Value = "Canon EOS R6";
        _editor.IsDirty.ShouldBeTrue();
        _editor.SaveCommand.CanExecute(null).ShouldBeTrue();

        _editor.RevertCommand.Execute(null);

        _editor.IsDirty.ShouldBeFalse();
        Field("IFD0:Model").Value.ShouldBe("Canon EOS R5");
    }

    [Fact]
    public async Task Saves_through_preview_then_writer_then_reloads()
    {
        await _editor.LoadAsync(Metadata.Path);
        Field("MWG:Keywords").Value = "London;Thames";
        Field("MWG:Description").Value = string.Empty;
        Field("MWG:Copyright").Value = "  (c) Dave  ";

        await _editor.SaveCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ConfirmChangesAsync(Arg.Is<ChangePreview>(p => p.ChangeCount == 3));
        var editSet = (EditSet)_writer.ReceivedCalls().Single().GetArguments()[0]!;
        var edits = editSet.Files.ShouldHaveSingleItem().Edits;
        edits.ShouldContain(e => e.Key == TagKey.Parse("MWG:Keywords") && e.Kind == EditKind.Set && e.Values.SequenceEqual(new[] { "London", "Thames" }));
        edits.ShouldContain(e => e.Key == TagKey.Parse("MWG:Description") && e.Kind == EditKind.Remove);
        edits.ShouldContain(e => e.Key == TagKey.Parse("MWG:Copyright") && e.Values.SequenceEqual(new[] { "(c) Dave" }));
        await _reader.Received(2).ReadAsync(Metadata.Path, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Does_not_write_when_the_preview_is_declined()
    {
        _dialogs.ConfirmChangesAsync(Arg.Any<ChangePreview>()).Returns(false);
        await _editor.LoadAsync(Metadata.Path);
        Field("IFD0:Model").Value = "Changed";

        await _editor.SaveCommand.ExecuteAsync(null);

        _writer.ReceivedCalls().ShouldBeEmpty();
        _editor.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public async Task Blocks_saving_an_invalid_value()
    {
        await _editor.LoadAsync(Metadata.Path);
        Field("MWG:DateTimeOriginal").Value = "15/06/2024";

        Field("MWG:DateTimeOriginal").HasError.ShouldBeTrue();
        await _editor.SaveCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ShowErrorAsync(Arg.Any<string>(), Arg.Is<string>(m => m.Contains("Date taken")));
        _writer.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Treats_a_formatting_only_change_as_no_change()
    {
        await _editor.LoadAsync(Metadata.Path);
        Field("MWG:Keywords").Value = "London;Big Ben";

        await _editor.SaveCommand.ExecuteAsync(null);

        await _dialogs.DidNotReceive().ConfirmChangesAsync(Arg.Any<ChangePreview>());
        _writer.ReceivedCalls().ShouldBeEmpty();
        _editor.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public async Task Reports_a_failed_write_and_keeps_the_edits()
    {
        _writer.ApplyAsync(Arg.Any<EditSet>(), Arg.Any<IProgress<WriteProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(call => new WriteResult(call.Arg<EditSet>().BatchId, [new FileWriteResult(Metadata.Path, false, "Disk full", [])], true));
        await _editor.LoadAsync(Metadata.Path);
        Field("IFD0:Model").Value = "Changed";

        await _editor.SaveCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ShowErrorAsync(Arg.Any<string>(), "Disk full");
        _editor.IsDirty.ShouldBeTrue();
    }

    [Theory]
    [InlineData(UnsavedChangesChoice.Discard, true, false)]
    [InlineData(UnsavedChangesChoice.Cancel, false, true)]
    [InlineData(UnsavedChangesChoice.Save, true, false)]
    public async Task Asks_about_unsaved_changes_before_leaving(UnsavedChangesChoice choice, bool canLeave, bool stillDirty)
    {
        _dialogs.AskToSaveChangesAsync("a.jpg").Returns(choice);
        await _editor.LoadAsync(Metadata.Path);
        Field("IFD0:Model").Value = "Changed";

        (await _editor.ConfirmLeaveAsync()).ShouldBe(canLeave);

        _editor.IsDirty.ShouldBe(stillDirty);
        _writer.ReceivedCalls().Count().ShouldBe(choice == UnsavedChangesChoice.Save ? 1 : 0);
    }

    [Fact]
    public async Task Leaving_without_changes_does_not_ask()
    {
        await _editor.LoadAsync(Metadata.Path);

        (await _editor.ConfirmLeaveAsync()).ShouldBeTrue();

        await _dialogs.DidNotReceive().AskToSaveChangesAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task Explains_why_several_photos_cannot_be_edited_here()
    {
        await _editor.ShowSelectionAsync([@"C:\a.jpg", @"C:\b.jpg"]);

        _editor.HasPhoto.ShouldBeFalse();
        _editor.Message.ShouldNotBeNull().ShouldContain("2 photos selected");
        _editor.Groups.ShouldBeEmpty();
    }

    [Fact]
    public async Task Ignores_a_slow_load_that_a_newer_selection_overtook()
    {
        var slow = new TaskCompletionSource<ImageMetadata>();
        _reader.ReadAsync(@"C:\slow.jpg", Arg.Any<CancellationToken>()).Returns(slow.Task);
        _reader.ReadAsync(@"C:\fast.jpg", Arg.Any<CancellationToken>()).Returns(Metadata.Sample(@"C:\fast.jpg"));

        var first = _editor.LoadAsync(@"C:\slow.jpg");
        await _editor.LoadAsync(@"C:\fast.jpg");
        slow.SetResult(Metadata.Sample(@"C:\slow.jpg"));
        await first;

        _editor.FilePath.ShouldBe(@"C:\fast.jpg");
        _editor.IsLoading.ShouldBeFalse();
    }

    [Fact]
    public async Task Shows_a_read_error_as_a_message()
    {
        _reader.ReadAsync(@"C:\bad.jpg", Arg.Any<CancellationToken>()).Returns<ImageMetadata>(_ => throw new IOException("File is empty"));

        await _editor.LoadAsync(@"C:\bad.jpg");

        _editor.HasPhoto.ShouldBeFalse();
        _editor.Message.ShouldBe("Couldn't read bad.jpg: File is empty");
    }

    [Fact]
    public async Task Filters_fields_by_name_key_or_value()
    {
        await _editor.LoadAsync(Metadata.Path);

        _editor.FilterText = "canon";

        _editor.Groups.SelectMany(g => g.Fields).Select(f => f.KeyText).ShouldBe(["IFD0:Model"]);

        _editor.FilterText = string.Empty;
        _editor.Groups.SelectMany(g => g.Fields).Count().ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task Filtering_keeps_unsaved_edits()
    {
        await _editor.LoadAsync(Metadata.Path);
        Field("IFD0:Model").Value = "Changed";

        _editor.FilterText = "exposure";
        _editor.FilterText = string.Empty;

        Field("IFD0:Model").Value.ShouldBe("Changed");
        _editor.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public void Remembers_the_show_tag_keys_choice()
    {
        _settings.SaveCount.ShouldBe(0);

        _editor.ShowTagKeys = true;

        _settings.Current.ShowTagKeys.ShouldBeTrue();
    }

    private TagFieldViewModel Field(string key) =>
        _editor.Groups.SelectMany(g => g.Fields).Single(f => f.Key == TagKey.Parse(key));

    private static WriteResult Succeeded(EditSet editSet) =>
        new(editSet.BatchId, [.. editSet.Files.Select(f => new FileWriteResult(f.FilePath, true, null, []))], true);
}
