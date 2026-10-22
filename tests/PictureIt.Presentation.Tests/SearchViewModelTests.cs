using CommunityToolkit.Mvvm.Messaging;
using PictureIt.Core.History;
using PictureIt.Core.Model;
using PictureIt.Core.Search;
using PictureIt.Presentation.Services;
using PictureIt.Presentation.ViewModels;

namespace PictureIt.Presentation.Tests;

public sealed class SearchViewModelTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "PictureIt.Tests", Guid.NewGuid().ToString("N"));
    private readonly Harness _h = new();
    private readonly LibraryViewModel _library;
    private readonly CatalogSync _sync;
    private readonly SearchViewModel _search;

    public SearchViewModelTests()
    {
        Directory.CreateDirectory(_folder);
        _h.Catalog.GetFoldersAsync(Arg.Any<CancellationToken>()).Returns([new CatalogFolder(_folder, DateTimeOffset.UtcNow, 2)]);
        _h.Catalog.GetFacetsAsync(Arg.Any<CancellationToken>())
            .Returns(new CatalogFacets([new FacetValue("Canon EOS R5", 2)], [], [new FacetValue("London", 1)]));
        _library = _h.CreateLibrary();
        _sync = _h.CreateCatalogSync();
        _search = _h.CreateSearch(_library, _sync);
    }

    private string Photo(string name)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    private void Returns(params CatalogEntry[] entries)
    {
        _h.Catalog.SearchAsync(Arg.Any<PhotoQuery>(), Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(entries);
        _h.Catalog.CountAsync(Arg.Any<PhotoQuery>(), Arg.Any<CancellationToken>()).Returns(entries.Length);
    }

    private static CatalogEntry Entry(string path, string? camera = "Canon EOS R5") => new() { FilePath = path, Camera = camera, DateTaken = new DateTime(2024, 6, 15) };

    [Fact]
    public async Task Loads_folders_filter_lists_and_results()
    {
        Returns(Entry(Photo("a.jpg")));

        await _search.LoadAsync();

        _search.Folders.Single().Summary.ShouldStartWith("2 photos · scanned");
        _search.Cameras.ShouldBe([SearchViewModel.Any, "Canon EOS R5"]);
        _search.Keywords.ShouldBe([SearchViewModel.Any, "London"]);
        _search.ResultCount.ShouldBe(1);
        _search.Summary.ShouldBe("1 photo · All photos");
        _search.Results.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Filters_build_the_query_and_search_as_they_change()
    {
        await _search.LoadAsync();

        _search.Text = " big ben ";
        _search.Camera = "Canon EOS R5";
        _search.CopyrightIndex = 2;
        _search.MinIso = 400;
        _search.TakenFrom = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        await Task.Yield();

        var query = _search.Query;
        query.Text.ShouldBe("big ben");
        query.Camera.ShouldBe("Canon EOS R5");
        query.Copyright.ShouldBe(Presence.Missing);
        query.MinIso.ShouldBe(400);
        query.MaxIso.ShouldBeNull();
        query.TakenFrom.ShouldBe(new DateOnly(2024, 6, 1));
        query.Lens.ShouldBeNull();
        await _h.Catalog.Received().CountAsync(Arg.Is<PhotoQuery>(q => q.Copyright == Presence.Missing && q.Text == "big ben"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Clearing_filters_returns_to_all_photos()
    {
        await _search.LoadAsync();
        _search.Text = "x";
        _search.LocationIndex = 1;

        _search.ClearFiltersCommand.Execute(null);

        _search.Query.ShouldBe(PhotoQuery.All);
    }

    [Fact]
    public async Task Saves_applies_and_deletes_searches()
    {
        await _search.LoadAsync();
        _search.Camera = "Canon EOS R5";
        _search.CopyrightIndex = 2;
        _h.Dialogs.PromptAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns("No copyright");

        await _search.SaveSearchCommand.ExecuteAsync(null);

        var saved = _h.SavedSearches.Searches.ShouldHaveSingleItem();
        saved.Name.ShouldBe("No copyright");
        saved.Query.Copyright.ShouldBe(Presence.Missing);

        _search.ClearFiltersCommand.Execute(null);
        _search.ApplySavedSearchCommand.Execute(saved);
        _search.Query.ShouldBe(saved.Query);

        _search.DeleteSavedSearchCommand.Execute(saved);
        _h.SavedSearches.Searches.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_saved_search_keeps_a_camera_no_longer_in_the_catalogue()
    {
        await _search.LoadAsync();

        _search.Apply(PhotoQuery.All with { Camera = "Nikon Z8" });

        _search.Camera.ShouldBe("Nikon Z8");
        _search.Cameras.ShouldContain("Nikon Z8");
    }

    [Fact]
    public async Task Show_in_Library_shows_every_result_there_and_switches_page()
    {
        var a = Photo("a.jpg");
        var b = Photo("b.jpg");
        Returns(Entry(a), Entry(b), Entry(Path.Combine(_folder, "deleted since.jpg")));
        NavigateMessage? navigated = null;
        _h.Messenger.Register<NavigateMessage>(this, (_, m) => navigated = m);
        await _search.LoadAsync();

        await _search.ShowInLibraryCommand.ExecuteAsync(null);

        _library.IsShowingResults.ShouldBeTrue();
        _library.FolderName.ShouldBe("Search results");
        _library.LocationText.ShouldBe("All photos");
        _library.Photos.Select(p => p.FilePath).ShouldBe([a, b]);
        navigated.ShouldNotBeNull().Page.ShouldBe(NavigateMessage.Library);
    }

    [Fact]
    public async Task Adding_a_folder_scans_it_reports_and_watches_it()
    {
        _h.Catalog.AddFolderAsync(_folder, Arg.Any<CancellationToken>()).Returns(_folder);
        _h.Catalog.ScanAsync(_folder, Arg.Any<IProgress<WriteProgress>?>(), Arg.Any<CancellationToken>()).Returns(new ScanResult(3, 0, 0, 0, []));
        _h.Dialogs.RunWithProgressAsync(Arg.Any<string>(), Arg.Any<Func<IProgress<WriteProgress>, CancellationToken, Task<ScanResult>>>())
            .Returns(call => call.Arg<Func<IProgress<WriteProgress>, CancellationToken, Task<ScanResult>>>()(new Progress<WriteProgress>(), CancellationToken.None));

        await _search.AddFolderAsync(_folder);

        await _h.Catalog.Received(1).ScanAsync(_folder, Arg.Any<IProgress<WriteProgress>?>(), Arg.Any<CancellationToken>());
        _search.Message.ShouldBe($"Scanned {Path.GetFileName(_folder)}: 3 new, 0 updated, 0 removed.");
        _h.Watchers.Watched.ShouldContain(_folder);
    }

    [Fact]
    public async Task A_cancelled_scan_says_so()
    {
        _h.Dialogs.RunWithProgressAsync(Arg.Any<string>(), Arg.Any<Func<IProgress<WriteProgress>, CancellationToken, Task<ScanResult>>>())
            .Returns<ScanResult>(_ => throw new OperationCanceledException());
        await _search.LoadAsync();

        await _search.RescanCommand.ExecuteAsync(_search.Folders[0]);

        _search.Message.ShouldNotBeNull().ShouldContain("stopped");
    }

    [Fact]
    public async Task Removing_a_folder_asks_first()
    {
        await _search.LoadAsync();
        _h.Dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        await _search.RemoveFolderCommand.ExecuteAsync(_search.Folders[0]);
        await _h.Catalog.DidNotReceive().RemoveFolderAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        _h.Dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        await _search.RemoveFolderCommand.ExecuteAsync(_search.Folders[0]);
        await _h.Catalog.Received(1).RemoveFolderAsync(_folder, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Statistics_are_charted_for_the_results()
    {
        Returns(Entry(Photo("a.jpg")), Entry(Photo("b.jpg"), camera: "Nikon Z8"));
        await _search.LoadAsync();

        _search.ShowStatistics = true;
        await _search.LoadStatisticsCommand.ExecuteAsync(null);

        _search.ShowPhotos.ShouldBeFalse();
        var cameras = _search.Statistics.ShouldNotBeNull().Charts.Single(c => c.Title == "Cameras");
        cameras.Bars.Select(b => (b.Label, b.Count)).ShouldBe([("Canon EOS R5", 1), ("Nikon Z8", 1)]);
    }

    public void Dispose()
    {
        _sync.Dispose();
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

public sealed class CatalogSyncTests : IDisposable
{
    private readonly Harness _h = new();
    private readonly CatalogSync _sync;
    private int _changed;

    public CatalogSyncTests()
    {
        _sync = _h.CreateCatalogSync();
        _h.Messenger.Register<CatalogChangedMessage>(this, (_, _) => _changed++);
    }

    [Fact]
    public async Task Refreshes_files_the_app_writes()
    {
        _h.Messenger.Send(new FilesChangedMessage([@"C:\photos\a.jpg"]));
        await _sync.Idle;

        await _h.Catalog.Received(1).RefreshFilesAsync(Arg.Is<IReadOnlyCollection<string>>(p => p.Single() == @"C:\photos\a.jpg"), Arg.Any<CancellationToken>());
        _changed.ShouldBe(1);
    }

    [Fact]
    public async Task Follows_renames_and_moves()
    {
        _h.Messenger.Send(new FolderChangedMessage([new FileMove(@"C:\a.jpg", @"C:\b.jpg")]));
        await _sync.Idle;

        await _h.Catalog.Received(1).ApplyMovesAsync(Arg.Is<IReadOnlyList<FileMove>>(m => m.Single().To == @"C:\b.jpg"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Watches_catalogue_folders_and_refreshes_what_changed_outside()
    {
        _h.Catalog.GetFoldersAsync(Arg.Any<CancellationToken>()).Returns([new CatalogFolder(Path.GetTempPath().TrimEnd('\\'), null, 0)]);
        await _sync.WatchFoldersAsync();
        var root = _h.Watchers.Watched.Single();

        _h.Watchers.Fire(root, Path.Combine(root, "new.jpg"));
        await _sync.Idle;
        await _h.Catalog.Received(1).RefreshFilesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());

        // A folder changed (or too much happened at once): rescan.
        _h.Watchers.Fire(root, Path.Combine(root, "Renamed folder"));
        await _sync.Idle;
        await _h.Catalog.Received(1).ScanAsync(root, Arg.Any<IProgress<WriteProgress>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Background_failures_are_kept_not_thrown()
    {
        _h.Catalog.RefreshFilesAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns<int>(_ => throw new IOException("disk gone"));

        _h.Messenger.Send(new FilesChangedMessage([@"C:\a.jpg"]));
        await _sync.Idle;

        _sync.LastError.ShouldBe("disk gone");
    }

    public void Dispose()
    {
        _h.Messenger.UnregisterAll(this);
        _sync.Dispose();
    }
}
