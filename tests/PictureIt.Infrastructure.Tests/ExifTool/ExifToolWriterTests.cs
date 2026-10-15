using System.Security.Cryptography;
using PictureIt.Core.Model;
using PictureIt.Infrastructure.Backup;
using PictureIt.Infrastructure.ExifTool;

namespace PictureIt.Infrastructure.Tests.ExifTool;

[Collection(ExifToolCollectionDefinition.Name)]
public sealed class ExifToolWriterTests : IDisposable
{
    private static readonly TagKey Title = TagKey.Parse("XMP-dc:Title");
    private static readonly TagKey Subject = TagKey.Parse("XMP-dc:Subject");

    private readonly ExifToolFixture _exifTool;
    private readonly TestFiles _files = new();
    private readonly FileBackupStore _backups;
    private readonly ExifToolWriter _writer;
    private readonly ExifToolReader _reader;

    public ExifToolWriterTests(ExifToolFixture exifTool)
    {
        _exifTool = exifTool;
        _backups = new FileBackupStore(Path.Combine(_files.Directory, "_backups"));
        _writer = new ExifToolWriter(exifTool.Process, _backups);
        _reader = new ExifToolReader(exifTool.Process);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sets_a_value()
    {
        var path = _files.Copy(TestFiles.SampleJpeg);

        var result = await Apply(path, MetadataEdit.Set(TagKey.Parse("IFD0:Artist"), "Dave Corbett"));

        result.Files.ShouldHaveSingleItem().Succeeded.ShouldBeTrue();
        (await Read(path)).GetValue("IFD0:Artist").ShouldBe("Dave Corbett");
    }

    [Theory]
    [InlineData("Line one\nLine two")]
    [InlineData("Windows\r\nline breaks")]
    [InlineData(@"Back\slash and \\ double, plus ""quotes""")]
    [InlineData("  leading and trailing spaces  ")]
    [InlineData("Unicode: été, Zürich, 東京, 📷")]
    [InlineData("-looks like an option")]
    [InlineData("#looks like a comment")]
    public async Task Writes_awkward_values_exactly(string value)
    {
        var path = _files.Copy(TestFiles.SampleJpeg);

        var result = await Apply(path, MetadataEdit.Set(TagKey.Parse("XMP-dc:Description"), value));

        result.Files.ShouldHaveSingleItem().Error.ShouldBeNull();
        (await Read(path)).GetValue("XMP-dc:Description").ShouldBe(value);
    }

    [Fact]
    public async Task Adds_and_removes_list_items_without_replacing_the_list()
    {
        var path = _files.Copy(TestFiles.SampleJpeg);

        await Apply(path, MetadataEdit.AddToList(Subject, "Thames", "Westminster"), MetadataEdit.RemoveFromList(Subject, "Big Ben"));

        ListValues(await Read(path), Subject).ShouldBe(["London", "Thames", "Westminster"]);
    }

    [Fact]
    public async Task Set_replaces_a_whole_list()
    {
        var path = _files.Copy(TestFiles.SampleJpeg);

        await Apply(path, MetadataEdit.Set(Subject, ["Paris", "Eiffel Tower"]));

        ListValues(await Read(path), Subject).ShouldBe(["Paris", "Eiffel Tower"]);
    }

    [Fact]
    public async Task Removes_a_tag()
    {
        var path = _files.Copy(TestFiles.SampleJpeg);

        var result = await Apply(path, MetadataEdit.Remove(TagKey.Parse("ExifIFD:SerialNumber")));

        result.Files.ShouldHaveSingleItem().Succeeded.ShouldBeTrue();
        (await Read(path)).GetValue("ExifIFD:SerialNumber").ShouldBeNull();
    }

    [Fact]
    public async Task Leaves_image_data_untouched()
    {
        var path = _files.Copy(TestFiles.SampleJpeg);
        var before = await _exifTool.ImageDataHashAsync(path);

        await Apply(path, MetadataEdit.Set(Title, "New title"), MetadataEdit.Remove(TagKey.Parse("IFD0:Make")));

        (await _exifTool.ImageDataHashAsync(path)).ShouldBe(before);
        before.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Preserves_the_file_modified_date_by_default()
    {
        var path = _files.Copy(TestFiles.SampleJpeg);
        var stamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);

        await Apply(path, MetadataEdit.Set(Title, "Dated"));

        File.GetLastWriteTimeUtc(path).ShouldBe(stamp);
    }

    [Fact]
    public async Task Continues_past_failures_and_reports_them_per_file()
    {
        var first = _files.Copy(TestFiles.SampleJpeg, "first.jpg");
        var missing = _files.PathFor("missing.jpg");
        var notAnImage = _files.PathFor("notes.jpg");
        await File.WriteAllTextAsync(notAnImage, "not really a jpeg", Ct);
        var last = _files.Copy(TestFiles.SampleJpeg, "last.jpg");
        var edit = MetadataEdit.Set(Title, "Batch");

        var result = await _writer.ApplyAsync(
            EditSet.Create("test", new[] { first, missing, notAnImage, last }.Select(p => new FileEdits(p, [edit]))),
            cancellationToken: Ct);

        result.Files.Select(f => f.Succeeded).ShouldBe([true, false, false, true]);
        result.Files[1].Error.ShouldBe("File not found.");
        result.Files[2].Error.ShouldNotBeNullOrWhiteSpace();
        (await Read(last)).GetValue(Title.ToString()).ShouldBe("Batch");
    }

    [Fact]
    public async Task Reports_an_unknown_tag_as_a_failure()
    {
        var path = _files.Copy(TestFiles.SampleJpeg);

        var result = await Apply(path, MetadataEdit.Set(TagKey.Parse("XMP-dc:NoSuchTag"), "x"));

        var file = result.Files.ShouldHaveSingleItem();
        file.Succeeded.ShouldBeFalse();
        file.Error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Backs_up_first_so_a_batch_can_be_restored_byte_for_byte()
    {
        var paths = new[] { _files.Copy(TestFiles.SampleJpeg, "a.jpg"), _files.Copy(TestFiles.SampleJpeg, "b.jpg") };
        var originals = paths.Select(Hash).ToArray();

        var result = await _writer.ApplyAsync(
            EditSet.Create("test", paths.Select(p => new FileEdits(p, [MetadataEdit.Set(Title, "Changed")]))),
            cancellationToken: Ct);

        result.BackedUp.ShouldBeTrue();
        paths.Select(Hash).ShouldNotBe(originals);

        await _backups.RestoreAsync(result.BatchId, Ct);

        paths.Select(Hash).ShouldBe(originals);
    }

    [Fact]
    public async Task Reports_progress()
    {
        var paths = new[] { _files.Copy(TestFiles.SampleJpeg, "a.jpg"), _files.Copy(TestFiles.SampleJpeg, "b.jpg") };
        var reports = new List<WriteProgress>();

        await _writer.ApplyAsync(
            EditSet.Create("test", paths.Select(p => new FileEdits(p, [MetadataEdit.Set(Title, "x")]))),
            new SynchronousProgress<WriteProgress>(reports.Add),
            Ct);

        reports.Select(r => r.Completed).ShouldBe([0, 1, 2]);
        reports.ShouldAllBe(r => r.Total == 2);
    }

    [Fact]
    public async Task Cancelling_before_writing_leaves_files_unchanged()
    {
        var path = _files.Copy(TestFiles.SampleJpeg);
        var original = Hash(path);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var result = await new ExifToolWriter(_exifTool.Process, backupStore: null).ApplyAsync(
            EditSet.Create("test", [new FileEdits(path, [MetadataEdit.Set(Title, "x")])]),
            cancellationToken: cancelled.Token);

        result.Files.ShouldHaveSingleItem().Error.ShouldBe("Cancelled.");
        Hash(path).ShouldBe(original);
    }

    private Task<WriteResult> Apply(string path, params MetadataEdit[] edits) =>
        _writer.ApplyAsync(EditSet.Create("test", [new FileEdits(path, edits)]), cancellationToken: Ct);

    private Task<ImageMetadata> Read(string path) => _reader.ReadAsync(path, Ct);

    private static IReadOnlyList<string> ListValues(ImageMetadata metadata, TagKey key)
    {
        metadata.TryGetTag(key, out var tag).ShouldBeTrue();
        return tag.Values;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    public void Dispose() => _files.Dispose();

    /// <summary><see cref="Progress{T}"/> posts to the thread pool; this reports inline so order is deterministic.</summary>
    private sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
