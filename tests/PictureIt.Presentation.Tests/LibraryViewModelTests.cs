using CommunityToolkit.Mvvm.Messaging;
using PictureIt.Core.Abstractions;
using PictureIt.Core.Model;
using PictureIt.Presentation.Services;
using PictureIt.Presentation.ViewModels;

namespace PictureIt.Presentation.Tests;

public sealed class LibraryViewModelTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "PictureIt.Tests", Guid.NewGuid().ToString("N"));
    private readonly Harness _h = new();
    private readonly IDialogService _dialogs;
    private readonly IFolderPicker _picker;
    private readonly IPhotoSummaryReader _summaries = Substitute.For<IPhotoSummaryReader>();
    private readonly InMemorySettingsStore _settings;
    private readonly LibraryViewModel _library;

    public LibraryViewModelTests()
    {
        Directory.CreateDirectory(_folder);
        foreach (var name in new[] { "b.JPG", "a.jpg", "c.cr3", "notes.txt", "d.png" })
        {
            var path = Path.Combine(_folder, name);
            File.WriteAllText(path, string.Empty);
            _h.Reader.Add(Metadata.Sample(path));
        }

        _dialogs = _h.Dialogs;
        _picker = _h.FolderPicker;
        _settings = _h.Settings;
        _library = _h.CreateLibrary(_summaries);
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
        _library.Editor.PhotoCount.ShouldBe(2);
        _library.Editor.Title.ShouldBe("2 photos");
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

    [Fact]
    public async Task Re_reads_the_folder_and_keeps_renamed_photos_selected()
    {
        await _library.LoadFolderAsync(_folder);
        var a = _library.Photos.First(p => p.FileName == "a.jpg");
        await _library.SelectAsync([a]);
        var renamed = Path.Combine(_folder, "renamed.jpg");
        File.Move(a.FilePath, renamed);
        IReadOnlyList<PhotoItemViewModel>? requested = null;
        _library.SelectionRequested += (_, photos) => requested = photos;

        // What the FolderChangedMessage handler does.
        await _library.RefreshFolderAsync([new PictureIt.Core.History.FileMove(a.FilePath, renamed)]);

        _library.Photos.Select(p => p.FileName).ShouldContain("renamed.jpg");
        _library.Photos.Select(p => p.FileName).ShouldNotContain("a.jpg");
        requested.ShouldNotBeNull().Single().FilePath.ShouldBe(renamed);
        _h.Organise.CurrentFolder.ShouldBe(_folder);
    }

    [Fact]
    public async Task Notices_photos_added_by_other_apps()
    {
        await _library.LoadFolderAsync(_folder);
        _h.Watchers.Watched.ShouldBe([_folder]);
        var added = Path.Combine(_folder, "added.jpg");
        File.WriteAllText(added, string.Empty);

        _h.Watchers.Fire(_folder, added);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        _library.Photos.Select(p => p.FileName).ShouldContain("added.jpg");
    }

    [Fact]
    public async Task Shows_search_results_from_any_folders_and_refreshes_them()
    {
        await _library.LoadFolderAsync(_folder);
        var a = Path.Combine(_folder, "a.jpg");
        var d = Path.Combine(_folder, "d.png");
        IReadOnlyList<string> latest = [a, d];

        (await _library.ShowResultsAsync("Canon EOS R5", [a], () => Task.FromResult(latest))).ShouldBeTrue();

        _library.IsShowingResults.ShouldBeTrue();
        _library.CurrentFolder.ShouldBeNull();
        _library.HasFolder.ShouldBeTrue();
        _library.LocationText.ShouldBe("Canon EOS R5");
        _library.Photos.Select(p => p.FilePath).ShouldBe([a]);
        _h.Watchers.Watched.ShouldBeEmpty();
        _h.Organise.ImportMetadataCommand.CanExecute(null).ShouldBeTrue();

        // The catalogue changed: the search runs again.
        _h.Messenger.Send(new CatalogChangedMessage());
        await Task.Delay(100, TestContext.Current.CancellationToken);
        _library.Photos.Select(p => p.FilePath).ShouldBe([a, d]);

        // Opening a folder leaves the results.
        await _library.LoadFolderAsync(_folder);
        _library.IsShowingResults.ShouldBeFalse();
    }

    [Fact]
    public async Task Results_follow_moved_photos()
    {
        var a = Path.Combine(_folder, "a.jpg");
        await _library.ShowResultsAsync("x", [a], () => Task.FromResult<IReadOnlyList<string>>([a]));
        var moved = Path.Combine(_folder, "moved.jpg");
        File.Move(a, moved);

        await _library.RefreshFolderAsync([new PictureIt.Core.History.FileMove(a, moved)]);

        _library.Photos.Single().FilePath.ShouldBe(moved);
    }

    public void Dispose()
    {
        _library.Dispose();
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
