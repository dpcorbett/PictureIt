using PictureIt.Core.Abstractions;
using PictureIt.Core.Model;
using PictureIt.Presentation.Services;
using PictureIt.Presentation.ViewModels;

namespace PictureIt.Presentation.Tests;

public sealed class LibraryViewModelTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "PictureIt.Tests", Guid.NewGuid().ToString("N"));
    private readonly IMetadataReader _reader = Substitute.For<IMetadataReader>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly IFolderPicker _picker = Substitute.For<IFolderPicker>();
    private readonly IPhotoSummaryReader _summaries = Substitute.For<IPhotoSummaryReader>();
    private readonly InMemorySettingsStore _settings = new();
    private readonly LibraryViewModel _library;

    public LibraryViewModelTests()
    {
        Directory.CreateDirectory(_folder);
        foreach (var name in new[] { "b.JPG", "a.jpg", "c.cr3", "notes.txt", "d.png" })
        {
            File.WriteAllText(Path.Combine(_folder, name), string.Empty);
        }

        _reader.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call => Metadata.Sample(call.Arg<string>()));
        var editor = new MetadataEditorViewModel(_reader, Substitute.For<IMetadataWriter>(), _dialogs, _settings);
        _library = new LibraryViewModel(_picker, _settings, _summaries, editor);
    }

    [Fact]
    public async Task Lists_supported_photos_in_name_order()
    {
        (await _library.LoadFolderAsync(_folder)).ShouldBeTrue();

        _library.Photos.Select(p => p.FileName).ShouldBe(["a.jpg", "b.JPG", "c.cr3", "d.png"]);
        _library.CurrentFolder.ShouldBe(_folder);
        _library.StatusText.ShouldBe("4 photos");
    }

    [Fact]
    public async Task Remembers_opened_folders()
    {
        await _library.LoadFolderAsync(_folder);

        _settings.Current.RecentFolders.ShouldBe([_folder]);
        _library.RecentFolders.ShouldBe([_folder]);
    }

    [Fact]
    public async Task Opens_a_folder_from_the_picker()
    {
        _picker.PickFolderAsync().Returns(_folder);

        await _library.OpenFolderCommand.ExecuteAsync(null);

        _library.Photos.Count.ShouldBe(4);
    }

    [Fact]
    public async Task Cancelling_the_picker_changes_nothing()
    {
        _picker.PickFolderAsync().Returns((string?)null);

        await _library.OpenFolderCommand.ExecuteAsync(null);

        _library.HasFolder.ShouldBeFalse();
    }

    [Fact]
    public async Task Forgets_a_recent_folder_that_no_longer_exists()
    {
        var gone = Path.Combine(_folder, "gone");
        _settings.Current = _settings.Current.WithRecentFolder(gone);

        (await _library.LoadFolderAsync(gone)).ShouldBeFalse();

        _settings.Current.RecentFolders.ShouldBeEmpty();
        _library.StatusText.ShouldContain("not found");
    }

    [Fact]
    public async Task Selecting_one_photo_loads_it_and_shows_its_summary()
    {
        await _library.LoadFolderAsync(_folder);
        var photo = _library.Photos[0];
        _summaries.ReadSummary(photo.FilePath).Returns(new PhotoSummary { CameraModel = "Canon EOS R5", Iso = 400 });

        (await _library.SelectAsync([photo])).ShouldBeTrue();

        _library.Editor.FilePath.ShouldBe(photo.FilePath);
        _library.StatusText.ShouldBe("Canon EOS R5 · ISO 400");
    }

    [Fact]
    public async Task Selecting_several_photos_shows_a_count()
    {
        await _library.LoadFolderAsync(_folder);

        await _library.SelectAsync([_library.Photos[0], _library.Photos[1]]);

        _library.StatusText.ShouldBe("2 of 4 photos selected");
        _library.Editor.HasPhoto.ShouldBeFalse();
    }

    [Fact]
    public async Task Keeps_the_selection_when_the_user_cancels_over_unsaved_changes()
    {
        await _library.LoadFolderAsync(_folder);
        await _library.SelectAsync([_library.Photos[0]]);
        _library.Editor.Groups[0].Fields[0].Value = "Edited title";
        _dialogs.AskToSaveChangesAsync(Arg.Any<string>()).Returns(UnsavedChangesChoice.Cancel);

        (await _library.SelectAsync([_library.Photos[1]])).ShouldBeFalse();
        (await _library.LoadFolderAsync(_folder)).ShouldBeFalse();

        _library.Editor.FilePath.ShouldBe(_library.Photos[0].FilePath);
        _library.SelectedPhotos.ShouldBe([_library.Photos[0]]);
    }

    [Fact]
    public async Task Opening_photos_opens_their_folder_and_requests_their_selection()
    {
        IReadOnlyList<PhotoItemViewModel>? requested = null;
        _library.SelectionRequested += (_, photos) => requested = photos;

        await _library.OpenPathsAsync([Path.Combine(_folder, "b.JPG"), Path.Combine(_folder, "d.png")]);

        _library.CurrentFolder.ShouldBe(_folder);
        requested.ShouldNotBeNull().Select(p => p.FileName).ShouldBe(["b.JPG", "d.png"]);
    }

    [Fact]
    public async Task Keeps_a_selection_request_until_the_view_takes_it()
    {
        await _library.OpenPathsAsync([Path.Combine(_folder, "c.cr3")]);

        _library.TakePendingSelection().ShouldNotBeNull().ShouldHaveSingleItem().FileName.ShouldBe("c.cr3");
        _library.TakePendingSelection().ShouldBeNull();
    }

    [Fact]
    public async Task Opening_a_folder_path_opens_it_without_selecting()
    {
        var requested = false;
        _library.SelectionRequested += (_, _) => requested = true;

        await _library.OpenPathsAsync([_folder]);

        _library.Photos.Count.ShouldBe(4);
        requested.ShouldBeFalse();
    }

    [Fact]
    public async Task Opening_paths_that_do_not_exist_does_nothing()
    {
        await _library.OpenPathsAsync([Path.Combine(_folder, "missing.jpg")]);

        _library.HasFolder.ShouldBeFalse();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
