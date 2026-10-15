using PictureIt.Infrastructure.Backup;

namespace PictureIt.Infrastructure.Tests.Backup;

public sealed class FileBackupStoreTests : IDisposable
{
    private readonly TestFiles _files = new();
    private readonly FileBackupStore _store;

    public FileBackupStoreTests()
    {
        _store = new FileBackupStore(Path.Combine(_files.Directory, "_backups"));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Restores_files_and_timestamps()
    {
        var path = _files.Copy(TestFiles.SampleJpeg);
        var stamp = new DateTime(2021, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);
        var original = await File.ReadAllBytesAsync(path, Ct);
        var batchId = Guid.NewGuid();

        await _store.CreateAsync(batchId, [path], Ct);
        await File.WriteAllTextAsync(path, "overwritten", Ct);
        await _store.RestoreAsync(batchId, Ct);

        (await File.ReadAllBytesAsync(path, Ct)).ShouldBe(original);
        File.GetLastWriteTimeUtc(path).ShouldBe(stamp);
    }

    [Fact]
    public async Task Restores_a_deleted_file()
    {
        var path = _files.Copy(TestFiles.SampleJpeg);
        var batchId = Guid.NewGuid();

        await _store.CreateAsync(batchId, [path], Ct);
        File.Delete(path);
        await _store.RestoreAsync(batchId, Ct);

        File.Exists(path).ShouldBeTrue();
    }

    [Fact]
    public async Task Records_a_manifest_that_can_be_read_back()
    {
        var a = _files.Copy(TestFiles.SampleJpeg, "a.jpg");
        var b = _files.Copy(TestFiles.SampleJpeg, "b.jpg");
        var batchId = Guid.NewGuid();

        var created = await _store.CreateAsync(batchId, [a, b], Ct);
        var loaded = (await _store.GetAsync(batchId, Ct)).ShouldNotBeNull();

        loaded.BatchId.ShouldBe(batchId);
        loaded.Entries.Select(e => e.OriginalPath).ShouldBe([a, b]);
        loaded.Entries.ShouldAllBe(e => File.Exists(e.BackupPath) && e.Length == new FileInfo(e.OriginalPath).Length);
        loaded.CreatedAt.ShouldBe(created.CreatedAt);
    }

    [Fact]
    public async Task Refuses_to_overwrite_an_existing_backup()
    {
        var path = _files.Copy(TestFiles.SampleJpeg);
        var batchId = Guid.NewGuid();
        await _store.CreateAsync(batchId, [path], Ct);

        await Should.ThrowAsync<InvalidOperationException>(() => _store.CreateAsync(batchId, [path], Ct));
    }

    [Fact]
    public async Task Leaves_nothing_behind_when_a_file_cannot_be_backed_up()
    {
        var batchId = Guid.NewGuid();

        await Should.ThrowAsync<FileNotFoundException>(() =>
            _store.CreateAsync(batchId, [_files.Copy(TestFiles.SampleJpeg), _files.PathFor("missing.jpg")], Ct));

        (await _store.GetAsync(batchId, Ct)).ShouldBeNull();
        Directory.Exists(Path.Combine(_store.RootDirectory, batchId.ToString("N"))).ShouldBeFalse();
    }

    [Fact]
    public async Task Delete_removes_the_backup()
    {
        var batchId = Guid.NewGuid();
        await _store.CreateAsync(batchId, [_files.Copy(TestFiles.SampleJpeg)], Ct);

        await _store.DeleteAsync(batchId, Ct);

        (await _store.GetAsync(batchId, Ct)).ShouldBeNull();
    }

    public void Dispose() => _files.Dispose();
}
