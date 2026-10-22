using PictureIt.Core.History;
using PictureIt.Core.Model;
using PictureIt.Core.Search;
using PictureIt.Infrastructure.Backup;
using PictureIt.Infrastructure.ExifTool;
using PictureIt.Infrastructure.Search;

namespace PictureIt.Infrastructure.Tests.Search;

[Collection(ExifToolCollectionDefinition.Name)]
public sealed class SqlitePhotoCatalogTests : IDisposable
{
    private readonly TestFiles _files = new();
    private readonly ExifToolWriter _writer;
    private readonly ExifToolReader _reader;
    private readonly SqlitePhotoCatalog _catalog;
    private readonly string _photos;

    public SqlitePhotoCatalogTests(ExifToolFixture exifTool)
    {
        _writer = new ExifToolWriter(exifTool.Process, new FileBackupStore(Path.Combine(_files.Directory, "_backups")));
        _reader = new ExifToolReader(exifTool.Process);
        _catalog = new SqlitePhotoCatalog(_files.PathFor(@"db\catalog.db"), _reader);
        _photos = _files.PathFor("Photos");
        Directory.CreateDirectory(Path.Combine(_photos, "Trip"));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Add(string sample, string relative)
    {
        var path = Path.Combine(_photos, relative);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", sample), path);
        return path;
    }

    private async Task<ScanResult> ScanAsync()
    {
        var root = await _catalog.AddFolderAsync(_photos, Ct);
        return await _catalog.ScanAsync(root, cancellationToken: Ct);
    }

    private async Task<IEnumerable<string>> FindAsync(PhotoQuery query) =>
        (await _catalog.SearchAsync(query, cancellationToken: Ct)).Select(e => e.FileName).Order();

    [Fact]
    public async Task Indexes_photos_in_the_folder_and_below()
    {
        var bigBen = Add(TestFiles.SampleJpeg, "big ben.jpg");
        Add(TestFiles.SampleJpeg, @"Trip\copy.jpg");
        Add(TestFiles.SamplePng, "diagram.png");
        File.WriteAllText(Path.Combine(_photos, "notes.txt"), "not a photo");

        var result = await ScanAsync();

        result.Added.ShouldBe(3);
        result.Failed.ShouldBeEmpty();
        var entry = (await _catalog.SearchAsync(PhotoQuery.All with { Text = "big" }, cancellationToken: Ct)).First(e => e.FilePath == bigBen);
        entry.Camera.ShouldBe("Canon EOS R5");
        entry.Iso.ShouldBe(400);
        entry.FocalLength.ShouldBe(50);
        entry.FNumber.ShouldBe(2.8);
        entry.ExposureTime.ShouldNotBeNull().ShouldBe(0.004, 0.0001);
        entry.DateTaken.ShouldBe(new DateTime(2024, 6, 15, 14, 32, 10));
        entry.Keywords.ShouldBe(["London", "Big Ben"], ignoreOrder: true);
        entry.Latitude.ShouldNotBeNull().ShouldBe(51.5, 0.1);
        entry.Longitude.ShouldNotBeNull().ShouldBeLessThan(0);
        entry.Copyright.ShouldNotBeNull();

        var folder = (await _catalog.GetFoldersAsync(Ct)).ShouldHaveSingleItem();
        folder.PhotoCount.ShouldBe(3);
        folder.ScannedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Rescans_only_what_changed()
    {
        var a = Add(TestFiles.SampleJpeg, "a.jpg");
        var b = Add(TestFiles.SampleJpeg, "b.jpg");
        await ScanAsync();

        (await ScanAsync()).ShouldBe(new ScanResult(0, 0, 0, 2, []), new ScanResultComparer());

        File.SetLastWriteTimeUtc(a, DateTime.UtcNow.AddHours(-1));
        File.Delete(b);
        Add(TestFiles.SamplePng, "c.png");

        var result = await ScanAsync();
        (result.Added, result.Updated, result.Removed, result.Unchanged).ShouldBe((1, 1, 0 + 1, 0));
        (await FindAsync(PhotoQuery.All)).ShouldBe(["a.jpg", "c.png"]);
    }

    [Fact]
    public async Task Searches_by_text_keyword_camera_date_and_presence()
    {
        Add(TestFiles.SampleJpeg, "big ben.jpg");
        Add(TestFiles.SamplePng, @"Trip\diagram.png");
        await ScanAsync();

        (await FindAsync(PhotoQuery.All with { Text = "lond" })).ShouldBe(["big ben.jpg"]);
        (await FindAsync(PhotoQuery.All with { Text = "DIAGRAM" })).ShouldBe(["diagram.png"]);
        (await FindAsync(PhotoQuery.All with { Text = "\"big\" (ben" })).ShouldBe(["big ben.jpg"]);
        (await FindAsync(PhotoQuery.All with { Text = "nothing-like-this" })).ShouldBeEmpty();
        (await FindAsync(PhotoQuery.All with { Keyword = "big ben" })).ShouldBe(["big ben.jpg"]);
        (await FindAsync(PhotoQuery.All with { Camera = "canon eos r5" })).ShouldBe(["big ben.jpg"]);
        (await FindAsync(PhotoQuery.All with { TakenFrom = new DateOnly(2024, 6, 15), TakenTo = new DateOnly(2024, 6, 15) })).ShouldBe(["big ben.jpg"]);
        (await FindAsync(PhotoQuery.All with { TakenFrom = new DateOnly(2024, 6, 16) })).ShouldBeEmpty();
        (await FindAsync(PhotoQuery.All with { MinIso = 200, MaxIso = 400, MinFocalLength = 50, MaxFNumber = 4 })).ShouldBe(["big ben.jpg"]);
        (await FindAsync(PhotoQuery.All with { MinIso = 800 })).ShouldBeEmpty();
        (await FindAsync(PhotoQuery.All with { Location = Presence.Missing })).ShouldBe(["diagram.png"]);
        (await FindAsync(PhotoQuery.All with { Keywords = Presence.Has })).ShouldBe(["big ben.jpg"]);
        (await FindAsync(PhotoQuery.All with { Copyright = Presence.Missing })).ShouldBe(["diagram.png"]);
        (await FindAsync(PhotoQuery.All with { Folder = Path.Combine(_photos, "Trip") })).ShouldBe(["diagram.png"]);
        (await FindAsync(PhotoQuery.All with { Folder = _photos + "\\Tri" })).ShouldBeEmpty();
        (await _catalog.CountAsync(PhotoQuery.All, Ct)).ShouldBe(2);
    }

    [Fact]
    public async Task Lists_cameras_lenses_and_keywords_in_use()
    {
        Add(TestFiles.SampleJpeg, "a.jpg");
        Add(TestFiles.SampleJpeg, "b.jpg");
        await ScanAsync();

        var facets = await _catalog.GetFacetsAsync(Ct);

        facets.Cameras.ShouldBe([new FacetValue("Canon EOS R5", 2)]);
        facets.Lenses.ShouldHaveSingleItem().Count.ShouldBe(2);
        facets.Keywords.Select(k => k.Value).ShouldBe(["Big Ben", "London"]);
    }

    [Fact]
    public async Task Refreshing_a_file_picks_up_edits()
    {
        var path = Add(TestFiles.SampleJpeg, "a.jpg");
        await ScanAsync();

        // The writer keeps the modified date (-P), so a rescan wouldn't notice; the app refreshes files it writes.
        await _writer.ApplyAsync(EditSet.Create("t", [new FileEdits(path, [MetadataEdit.Set(TagKey.Parse("XMP-dc:Title"), "Clock tower")])]), cancellationToken: Ct);
        (await FindAsync(PhotoQuery.All with { Text = "clock" })).ShouldBeEmpty();

        (await _catalog.RefreshFilesAsync([path], Ct)).ShouldBe(1);

        (await FindAsync(PhotoQuery.All with { Text = "clock" })).ShouldBe(["a.jpg"]);
    }

    [Fact]
    public async Task Ignores_files_outside_the_catalogue_and_drops_deleted_ones()
    {
        var inside = Add(TestFiles.SampleJpeg, "a.jpg");
        await ScanAsync();
        var outside = _files.Copy(TestFiles.SampleJpeg, "outside.jpg");

        (await _catalog.RefreshFilesAsync([outside], Ct)).ShouldBe(0);

        File.Delete(inside);
        await _catalog.RefreshFilesAsync([inside], Ct);
        (await _catalog.CountAsync(PhotoQuery.All, Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Follows_renames_and_moves()
    {
        var a = Add(TestFiles.SampleJpeg, "a.jpg");
        await ScanAsync();
        var moved = Path.Combine(_photos, "Trip", "renamed.jpg");
        File.Move(a, moved);

        await _catalog.ApplyMovesAsync([new FileMove(a, moved)], Ct);

        (await _catalog.SearchAsync(PhotoQuery.All, cancellationToken: Ct)).Single().FilePath.ShouldBe(moved);
    }

    [Fact]
    public async Task Nested_folders_merge_into_one_catalogue_folder()
    {
        var trip = Path.Combine(_photos, "Trip");
        (await _catalog.AddFolderAsync(trip, Ct)).ShouldBe(trip);

        (await _catalog.AddFolderAsync(_photos, Ct)).ShouldBe(_photos);
        (await _catalog.AddFolderAsync(trip + "\\", Ct)).ShouldBe(_photos);

        (await _catalog.GetFoldersAsync(Ct)).Select(f => f.Path).ShouldBe([_photos]);
    }

    [Fact]
    public async Task Removing_a_folder_forgets_its_photos()
    {
        Add(TestFiles.SampleJpeg, "a.jpg");
        await ScanAsync();

        await _catalog.RemoveFolderAsync(_photos, Ct);

        (await _catalog.GetFoldersAsync(Ct)).ShouldBeEmpty();
        (await _catalog.CountAsync(PhotoQuery.All, Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Survives_reopening()
    {
        Add(TestFiles.SampleJpeg, "a.jpg");
        await ScanAsync();
        _catalog.Dispose();

        using var reopened = new SqlitePhotoCatalog(_files.PathFor(@"db\catalog.db"), _reader);

        (await reopened.CountAsync(PhotoQuery.All, Ct)).ShouldBe(1);
    }

    public void Dispose()
    {
        _catalog.Dispose();
        _files.Dispose();
    }

    private sealed class ScanResultComparer : IEqualityComparer<ScanResult>
    {
        public bool Equals(ScanResult? x, ScanResult? y) =>
            x is not null && y is not null && (x.Added, x.Updated, x.Removed, x.Unchanged, x.Failed.Count) == (y.Added, y.Updated, y.Removed, y.Unchanged, y.Failed.Count);

        public int GetHashCode(ScanResult obj) => obj.Added;
    }
}
