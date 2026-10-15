using PictureIt.Core.Model;
using PictureIt.Core.Templates;
using PictureIt.Presentation.ViewModels.Tools;

namespace PictureIt.Presentation.Tests;

public sealed class ToolsViewModelTests : IDisposable
{
    private const string A = @"C:\photos\a.jpg";
    private const string B = @"C:\photos\b.jpg";

    private readonly Harness _h = new();
    private readonly string _copyFolder = Path.Combine(Path.GetTempPath(), "PictureIt.Tests", Guid.NewGuid().ToString("N"));

    public ToolsViewModelTests()
    {
        _h.Reader.Add(Metadata.Create(A, ("MWG:Keywords", "London"), ("MWG:Description", "Trip to london"), ("ExifIFD:DateTimeOriginal", "2024:06:15 14:32:10"), ("GPS:GPSLatitude", "51 deg")));
        _h.Reader.Add(Metadata.Create(B, ("MWG:Keywords", "Paris"), ("ExifIFD:DateTimeOriginal", "2024:06:16 09:00:00")));
    }

    [Fact]
    public async Task Tools_need_photos_and_some_need_exactly_one()
    {
        _h.Tools.FindReplaceCommand.CanExecute(null).ShouldBeFalse();

        await _h.Editor.ShowSelectionAsync([A, B]);

        _h.Tools.FindReplaceCommand.CanExecute(null).ShouldBeTrue();
        _h.Tools.CopyMetadataCommand.CanExecute(null).ShouldBeFalse();
        _h.Tools.SaveAsTemplateCommand.CanExecute(null).ShouldBeFalse();
        _h.Tools.PasteMetadataCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task Applies_a_template_to_every_selected_photo()
    {
        await _h.Editor.ShowSelectionAsync([A, B]);
        var template = new MetadataTemplate("Trip", [new TemplateField(TagKey.Parse("MWG:Keywords"), AddToList: true, ["Holiday"])]);

        await _h.Tools.ApplyTemplateCommand.ExecuteAsync(template);

        var written = _h.Written.ShouldHaveSingleItem();
        written.Description.ShouldBe("Apply template \"Trip\" to 2 photos");
        var expected = MetadataEdit.AddToList(TagKey.Parse("MWG:Keywords"), "Holiday");
        written.Files.ShouldAllBe(f => f.Edits.Single() == expected);
    }

    [Fact]
    public async Task Find_and_replace_only_writes_photos_that_change()
    {
        await _h.Editor.ShowSelectionAsync([A, B]);
        _h.Dialogs.ShowToolAsync(Arg.Any<ToolViewModel>()).Returns(call =>
        {
            var tool = call.Arg<FindReplaceToolViewModel>();
            tool.Find = "london";
            tool.Replace = "London";
            return true;
        });

        await _h.Tools.FindReplaceCommand.ExecuteAsync(null);

        var file = _h.Written.ShouldHaveSingleItem().Files.ShouldHaveSingleItem();
        file.FilePath.ShouldBe(A);
        file.Edits.ShouldContain(MetadataEdit.Set(TagKey.Parse("MWG:Description"), "Trip to London"));
    }

    [Fact]
    public async Task Cancelling_a_tool_dialog_writes_nothing()
    {
        await _h.Editor.ShowSelectionAsync([A, B]);
        _h.Dialogs.ShowToolAsync(Arg.Any<ToolViewModel>()).Returns(false);

        await _h.Tools.ShiftDatesCommand.ExecuteAsync(null);

        _h.Written.ShouldBeEmpty();
    }

    [Fact]
    public async Task Shift_dates_uses_the_first_selected_photo_as_the_reference()
    {
        await _h.Editor.ShowSelectionAsync([B, A]);
        ShiftDatesToolViewModel? shown = null;
        _h.Dialogs.ShowToolAsync(Arg.Any<ToolViewModel>()).Returns(call =>
        {
            shown = call.Arg<ShiftDatesToolViewModel>();
            shown.UseReference = true;
            shown.CorrectTime = new TimeSpan(10, 0, 0);
            return true;
        });

        await _h.Tools.ShiftDatesCommand.ExecuteAsync(null);

        shown!.ReferenceName.ShouldBe("b.jpg");
        shown.Offset.ShouldBe(TimeSpan.FromHours(1));
        _h.Written.ShouldHaveSingleItem().Files.Single(f => f.FilePath == A).Edits
            .ShouldBe([MetadataEdit.Set(TagKey.Parse("ExifIFD:DateTimeOriginal"), "2024:06:15 15:32:10")]);
    }

    [Fact]
    public async Task Copies_from_one_photo_and_pastes_onto_another()
    {
        await _h.Editor.ShowSelectionAsync([A]);
        _h.Tools.CopyMetadataCommand.Execute(null);
        _h.Tools.CopiedFrom.ShouldBe("a.jpg");

        await _h.Editor.ShowSelectionAsync([B]);
        await _h.Tools.PasteMetadataCommand.ExecuteAsync(null);

        var edits = _h.Written.ShouldHaveSingleItem().Files.ShouldHaveSingleItem().Edits;
        edits.ShouldContain(MetadataEdit.Set(TagKey.Parse("MWG:Keywords"), "London"));
        edits.ShouldNotContain(e => e.Key == TagKey.Parse("MWG:DateTimeOriginal"));
    }

    [Fact]
    public async Task Saves_the_photo_as_a_template()
    {
        await _h.Editor.ShowSelectionAsync([A]);
        _h.Dialogs.PromptAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns("London trip");

        await _h.Tools.SaveAsTemplateCommand.ExecuteAsync(null);

        var template = _h.Templates.Templates.ShouldHaveSingleItem();
        template.Name.ShouldBe("London trip");
        _h.Tools.Templates.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Removes_metadata_from_copies_and_leaves_the_originals()
    {
        var original = Path.Combine(_copyFolder, "source", "a.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(original)!);
        File.WriteAllText(original, "photo");
        _h.Reader.Add(Metadata.Create(original, ("GPS:GPSLatitude", "51 deg")));
        await _h.Editor.ShowSelectionAsync([original]);
        var copies = Path.Combine(_copyFolder, "shared");
        _h.Dialogs.ShowToolAsync(Arg.Any<ToolViewModel>()).Returns(call =>
        {
            var tool = call.Arg<RemoveMetadataToolViewModel>();
            tool.SaveAsCopies = true;
            tool.CopyFolder = copies;
            return true;
        });

        await _h.Tools.RemoveMetadataCommand.ExecuteAsync(null);

        var copy = Path.Combine(copies, "a.jpg");
        File.ReadAllText(copy).ShouldBe("photo");
        _h.Written.ShouldHaveSingleItem().Files.ShouldHaveSingleItem().FilePath.ShouldBe(copy);
    }

    public void Dispose()
    {
        if (Directory.Exists(_copyFolder))
        {
            Directory.Delete(_copyFolder, recursive: true);
        }
    }
}
