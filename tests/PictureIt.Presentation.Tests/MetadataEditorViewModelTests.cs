using CommunityToolkit.Mvvm.Messaging;
using PictureIt.Core.Catalog;
using PictureIt.Core.Model;
using PictureIt.Presentation.Services;
using PictureIt.Presentation.ViewModels;

namespace PictureIt.Presentation.Tests;

public sealed class MetadataEditorViewModelTests
{
    private const string B = @"C:\photos\b.jpg";

    private readonly Harness _h = new();

    public MetadataEditorViewModelTests()
    {
        _h.Reader.Add(Metadata.Sample());
        _h.Reader.Add(Metadata.Create(
            B,
            ("IFD0:Model", "Canon EOS R5"),
            ("MWG:Keywords", "London; Thames"),
            ("MWG:Description", "Another description"),
            ("File:FileType", "JPEG")));
    }

    private MetadataEditorViewModel Editor => _h.Editor;

    [Fact]
    public async Task Shows_common_fields_first_even_when_the_file_lacks_them()
    {
        await Editor.LoadAsync(Metadata.Path);

        var common = Editor.Groups[0];
        common.Category.ShouldBe(TagCategory.Common);
        common.Fields.Select(f => f.Key).ShouldBe(TagCatalog.CommonFields.Select(f => f.Key));
        Field("MWG:Keywords").Value.ShouldBe("London; Big Ben");
        Field("MWG:Copyright").Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Groups_other_tags_by_category_in_display_order()
    {
        await Editor.LoadAsync(Metadata.Path);

        Editor.Groups.Select(g => g.Category).ShouldBe(
            [TagCategory.Common, TagCategory.Date, TagCategory.Location, TagCategory.Camera, TagCategory.Exposure, TagCategory.File]);
    }

    [Fact]
    public async Task Marks_computed_and_file_tags_read_only()
    {
        await Editor.LoadAsync(Metadata.Path);

        Field("File:FileType").IsReadOnly.ShouldBeTrue();
        Field("Composite:GPSPosition").IsReadOnly.ShouldBeTrue();
        Field("IFD0:Model").IsReadOnly.ShouldBeFalse();
    }

    [Fact]
    public async Task Tracks_edits_and_reverts_them()
    {
        await Editor.LoadAsync(Metadata.Path);

        Field("IFD0:Model").Value = "Canon EOS R6";
        Editor.IsDirty.ShouldBeTrue();
        Editor.SaveCommand.CanExecute(null).ShouldBeTrue();

        Editor.RevertCommand.Execute(null);

        Editor.IsDirty.ShouldBeFalse();
        Field("IFD0:Model").Value.ShouldBe("Canon EOS R5");
    }

    [Fact]
    public async Task Saves_through_the_batch_runner_then_reloads()
    {
        await Editor.LoadAsync(Metadata.Path);
        Field("MWG:Keywords").Value = "London;Thames";
        Field("MWG:Description").Value = string.Empty;
        Field("MWG:Copyright").Value = "  (c) Dave  ";

        await Editor.SaveCommand.ExecuteAsync(null);

        await _h.Dialogs.Received(1).ConfirmChangesAsync(Arg.Is<ChangePreview>(p => p.ChangeCount == 3));
        var edits = _h.Written.ShouldHaveSingleItem().Files.ShouldHaveSingleItem().Edits;
        edits.ShouldContain(MetadataEdit.Set(TagKey.Parse("MWG:Keywords"), ["London", "Thames"]));
        edits.ShouldContain(MetadataEdit.Remove(TagKey.Parse("MWG:Description")));
        edits.ShouldContain(MetadataEdit.Set(TagKey.Parse("MWG:Copyright"), "(c) Dave"));
        _h.History.Records.ShouldHaveSingleItem().Description.ShouldBe("Edit a.jpg");
        _h.Reader.ReadPaths.Count.ShouldBe(2);
        Editor.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public async Task Does_not_write_when_the_preview_is_declined()
    {
        _h.Dialogs.ConfirmChangesAsync(Arg.Any<ChangePreview>()).Returns(false);
        await Editor.LoadAsync(Metadata.Path);
        Field("IFD0:Model").Value = "Changed";

        await Editor.SaveCommand.ExecuteAsync(null);

        _h.Written.ShouldBeEmpty();
        Editor.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public async Task Blocks_saving_an_invalid_value()
    {
        await Editor.LoadAsync(Metadata.Path);
        Field("MWG:DateTimeOriginal").Value = "15/06/2024";

        Field("MWG:DateTimeOriginal").HasError.ShouldBeTrue();
        await Editor.SaveCommand.ExecuteAsync(null);

        await _h.Dialogs.Received(1).ShowErrorAsync(Arg.Any<string>(), Arg.Is<string>(m => m.Contains("Date taken")));
        _h.Written.ShouldBeEmpty();
    }

    [Fact]
    public async Task Treats_a_formatting_only_change_as_no_change()
    {
        await Editor.LoadAsync(Metadata.Path);
        Field("MWG:Keywords").Value = "London;Big Ben";

        await Editor.SaveCommand.ExecuteAsync(null);

        await _h.Dialogs.DidNotReceive().ConfirmChangesAsync(Arg.Any<ChangePreview>());
        _h.Written.ShouldBeEmpty();
        Editor.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public async Task Keeps_the_edits_when_the_write_fails()
    {
        _h.Writer.ApplyAsync(Arg.Any<EditSet>(), Arg.Any<IProgress<WriteProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(call => new WriteResult(call.Arg<EditSet>().BatchId, [new FileWriteResult(Metadata.Path, false, "Disk full", [])], true));
        await Editor.LoadAsync(Metadata.Path);
        Field("IFD0:Model").Value = "Changed";

        await Editor.SaveCommand.ExecuteAsync(null);

        await _h.Dialogs.Received(1).ShowWriteFailuresAsync(Arg.Is<WriteResult>(r => r.FailedCount == 1));
        Editor.IsDirty.ShouldBeTrue();
    }

    [Theory]
    [InlineData(UnsavedChangesChoice.Discard, true, false)]
    [InlineData(UnsavedChangesChoice.Cancel, false, true)]
    [InlineData(UnsavedChangesChoice.Save, true, false)]
    public async Task Asks_about_unsaved_changes_before_leaving(UnsavedChangesChoice choice, bool canLeave, bool stillDirty)
    {
        _h.Dialogs.AskToSaveChangesAsync("a.jpg").Returns(choice);
        await Editor.LoadAsync(Metadata.Path);
        Field("IFD0:Model").Value = "Changed";

        (await Editor.ConfirmLeaveAsync()).ShouldBe(canLeave);

        Editor.IsDirty.ShouldBe(stillDirty);
        _h.Written.Count.ShouldBe(choice == UnsavedChangesChoice.Save ? 1 : 0);
    }

    [Fact]
    public async Task Leaving_without_changes_does_not_ask()
    {
        await Editor.LoadAsync(Metadata.Path);

        (await Editor.ConfirmLeaveAsync()).ShouldBeTrue();

        await _h.Dialogs.DidNotReceive().AskToSaveChangesAsync(Arg.Any<string>());
    }

    [Fact]
    public async Task Ignores_a_slow_load_that_a_newer_selection_overtook()
    {
        _h.Reader.Gates[Metadata.Path] = new TaskCompletionSource();

        var first = Editor.LoadAsync(Metadata.Path);
        await Editor.LoadAsync(B);
        _h.Reader.Gates[Metadata.Path].SetResult();
        await first;

        Editor.FilePath.ShouldBe(B);
        Editor.IsLoading.ShouldBeFalse();
    }

    [Fact]
    public async Task Shows_a_read_error_as_a_message()
    {
        _h.Reader.Errors[@"C:\bad.jpg"] = "File is empty";

        await Editor.LoadAsync(@"C:\bad.jpg");

        Editor.HasPhoto.ShouldBeFalse();
        Editor.Message.ShouldBe("Couldn't read bad.jpg: File is empty");
    }

    [Fact]
    public async Task Filters_fields_by_name_key_or_value_and_keeps_edits()
    {
        await Editor.LoadAsync(Metadata.Path);
        Field("IFD0:Model").Value = "Changed";

        Editor.FilterText = "changed";
        Editor.Groups.SelectMany(g => g.Fields).Select(f => f.KeyText).ShouldBe(["IFD0:Model"]);

        Editor.FilterText = string.Empty;
        Field("IFD0:Model").Value.ShouldBe("Changed");
        Editor.IsDirty.ShouldBeTrue();
    }

    [Fact]
    public void Remembers_the_show_tag_keys_choice()
    {
        _h.Settings.SaveCount.ShouldBe(0);

        Editor.ShowTagKeys = true;

        _h.Settings.Current.ShowTagKeys.ShouldBeTrue();
    }

    // Several photos

    [Fact]
    public async Task Merges_several_photos_showing_shared_values_and_marking_differences()
    {
        await Editor.ShowSelectionAsync([Metadata.Path, B]);

        Editor.Title.ShouldBe("2 photos");
        Editor.IsMultiple.ShouldBeTrue();
        Field("IFD0:Model").Value.ShouldBe("Canon EOS R5");
        Field("IFD0:Model").IsMixed.ShouldBeFalse();
        Field("MWG:Description").IsMixed.ShouldBeTrue();
        Field("MWG:Description").Value.ShouldBeEmpty();
        Field("MWG:Keywords").IsMixed.ShouldBeTrue();
        Field("MWG:Keywords").Value.ShouldBe("London");
        Field("ExifIFD:DateTimeOriginal").IsMixed.ShouldBeTrue();
    }

    [Fact]
    public async Task Editing_a_mixed_field_sets_it_on_every_photo()
    {
        await Editor.ShowSelectionAsync([Metadata.Path, B]);
        Field("MWG:Description").Value = "Same for both";

        await Editor.SaveCommand.ExecuteAsync(null);

        var files = _h.Written.ShouldHaveSingleItem().Files;
        files.Select(f => f.FilePath).ShouldBe([Metadata.Path, B]);
        files.ShouldAllBe(f => f.Edits.Single() == MetadataEdit.Set(TagKey.Parse("MWG:Description"), "Same for both"));
        _h.History.Records.ShouldHaveSingleItem().Description.ShouldBe("Edit 2 photos");
    }

    [Fact]
    public async Task Editing_shared_keywords_adds_and_removes_without_touching_each_photos_others()
    {
        await Editor.ShowSelectionAsync([Metadata.Path, B]);
        Field("MWG:Keywords").Value = "England; UK";

        await Editor.SaveCommand.ExecuteAsync(null);

        var keywords = TagKey.Parse("MWG:Keywords");
        _h.Written.ShouldHaveSingleItem().Files[0].Edits.ShouldBe(
            [MetadataEdit.AddToList(keywords, "England", "UK"), MetadataEdit.RemoveFromList(keywords, "London")]);
    }

    [Fact]
    public async Task A_mixed_field_left_empty_changes_nothing_and_clear_all_removes_it()
    {
        await Editor.ShowSelectionAsync([Metadata.Path, B]);
        var description = Field("MWG:Description");
        description.IsDirty.ShouldBeFalse();

        description.ClearAllCommand.Execute(null);
        description.Placeholder.ShouldContain("removed");
        await Editor.SaveCommand.ExecuteAsync(null);

        _h.Written.ShouldHaveSingleItem().Files.ShouldAllBe(f => f.Edits.Single() == MetadataEdit.Remove(TagKey.Parse("MWG:Description")));
    }

    [Fact]
    public async Task Leaves_out_photos_that_cannot_be_read_and_says_so()
    {
        _h.Reader.Errors[B] = "File is empty";

        await Editor.ShowSelectionAsync([Metadata.Path, B]);

        Editor.PhotoCount.ShouldBe(1);
        Editor.Message.ShouldBeNull();
        Editor.Warning.ShouldBe("Couldn't read b.jpg, so it isn't included: File is empty");
    }

    [Fact]
    public async Task Reuses_earlier_reads_and_rereads_files_that_changed()
    {
        await Editor.ShowSelectionAsync([Metadata.Path]);
        await Editor.ShowSelectionAsync([Metadata.Path, B]);
        _h.Reader.ReadPaths.ShouldBe([Metadata.Path, B]);

        _h.Messenger.Send(new FilesChangedMessage([B]));
        await Task.Yield();

        _h.Reader.ReadPaths.ShouldBe([Metadata.Path, B, B]);
    }

    private TagFieldViewModel Field(string key) =>
        Editor.Groups.SelectMany(g => g.Fields).Single(f => f.Key == TagKey.Parse(key));
}
