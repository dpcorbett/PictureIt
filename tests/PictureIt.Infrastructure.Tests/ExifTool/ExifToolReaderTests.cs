using PictureIt.Core.Model;
using PictureIt.Infrastructure.ExifTool;

namespace PictureIt.Infrastructure.Tests.ExifTool;

[Collection(ExifToolCollectionDefinition.Name)]
public sealed class ExifToolReaderTests(ExifToolFixture exifTool) : IDisposable
{
    private readonly TestFiles _files = new();
    private readonly ExifToolReader _reader = new(exifTool.Process);

    [Fact]
    public async Task Reads_camera_and_exposure_tags()
    {
        var metadata = await _reader.ReadAsync(_files.Copy(TestFiles.SampleJpeg), TestContext.Current.CancellationToken);

        metadata.GetValue("IFD0:Make").ShouldBe("Canon");
        metadata.GetValue("IFD0:Model").ShouldBe("Canon EOS R5");
        metadata.GetValue("ExifIFD:LensModel").ShouldBe("RF50mm F1.8 STM");
        metadata.GetValue("ExifIFD:DateTimeOriginal").ShouldBe("2024:06:15 14:32:10");

        metadata.TryGetTag(TagKey.Parse("ExifIFD:ExposureTime"), out var exposure).ShouldBeTrue();
        exposure.DisplayValue.ShouldBe("1/250");
        exposure.RawValue.ShouldBe("0.004");
        exposure.Description.ShouldBe("Exposure Time");
    }

    [Fact]
    public async Task Reads_list_tags_as_lists()
    {
        var metadata = await _reader.ReadAsync(_files.Copy(TestFiles.SampleJpeg), TestContext.Current.CancellationToken);

        metadata.TryGetTag(TagKey.Parse("XMP-dc:Subject"), out var subject).ShouldBeTrue();
        subject.IsList.ShouldBeTrue();
        subject.Values.ShouldBe(["London", "Big Ben"]);

        metadata.TryGetTag(TagKey.Parse("IPTC:Keywords"), out var keywords).ShouldBeTrue();
        keywords.Values.ShouldBe(["London", "Big Ben"]);
    }

    [Fact]
    public async Task Reads_MWG_fields_as_editable()
    {
        var metadata = await _reader.ReadAsync(_files.Copy(TestFiles.SampleJpeg), TestContext.Current.CancellationToken);

        Tag(metadata, "MWG:Keywords").Values.ShouldBe(["London", "Big Ben"]);
        Tag(metadata, "MWG:Keywords").IsWritable.ShouldBeTrue();
        metadata.GetValue("MWG:Description").ShouldBe("Sample image for PictureIt tests");
        metadata.GetValue("MWG:Creator").ShouldBe("Test Photographer");
    }

    [Fact]
    public async Task Marks_a_one_item_list_as_a_list()
    {
        var path = _files.Copy(TestFiles.SampleJpeg);
        await exifTool.Process.ExecuteAsync(["-overwrite_original", "-XMP-dc:Subject=Solo", path], TestContext.Current.CancellationToken);

        var subject = Tag(await _reader.ReadAsync(path, TestContext.Current.CancellationToken), "XMP-dc:Subject");

        subject.IsList.ShouldBeTrue();
        subject.Values.ShouldBe(["Solo"]);
    }

    [Fact]
    public async Task Marks_computed_and_file_system_tags_read_only()
    {
        var metadata = await _reader.ReadAsync(_files.Copy(TestFiles.SampleJpeg), TestContext.Current.CancellationToken);

        Tag(metadata, "Composite:GPSPosition").IsWritable.ShouldBeFalse();
        Tag(metadata, "File:FileType").IsWritable.ShouldBeFalse();
        Tag(metadata, "IFD0:Model").IsWritable.ShouldBeTrue();
        Tag(metadata, "XMP-dc:Subject").IsWritable.ShouldBeTrue();
    }

    [Fact]
    public async Task Reads_files_with_non_ascii_paths()
    {
        var path = _files.Copy(TestFiles.SampleJpeg, "été 📷 Zürich.jpg");

        var metadata = await _reader.ReadAsync(path, TestContext.Current.CancellationToken);

        metadata.FilePath.ShouldBe(path);
        metadata.GetValue("IFD0:Model").ShouldBe("Canon EOS R5");
    }

    [Fact]
    public async Task ReadMany_reports_failures_per_file()
    {
        var good = _files.Copy(TestFiles.SampleJpeg);
        var png = _files.Copy(TestFiles.SamplePng);
        var missing = _files.PathFor("missing.jpg");
        var empty = _files.PathFor("empty.jpg");
        await File.WriteAllBytesAsync(empty, [], TestContext.Current.CancellationToken);

        var results = await _reader.ReadManyAsync([good, missing, png, empty], TestContext.Current.CancellationToken);

        results.Select(r => r.FilePath).ShouldBe([good, missing, png, empty]);
        results[0].Succeeded.ShouldBeTrue();
        results[1].Succeeded.ShouldBeFalse();
        results[1].Error.ShouldNotBeNull().ShouldContain("not found", Case.Insensitive);
        results[2].Succeeded.ShouldBeTrue();
        results[2].Metadata!.GetValue("File:FileType").ShouldBe("PNG");
        results[3].Succeeded.ShouldBeFalse();
        results[3].Error.ShouldBe("File is empty");
    }

    [Fact]
    public async Task Reads_a_corrupt_image_and_surfaces_the_warning()
    {
        // ExifTool reads what it can from a damaged file and flags it, rather than refusing it.
        var path = _files.PathFor("truncated.jpg");
        var bytes = await File.ReadAllBytesAsync(_files.Copy(TestFiles.SampleJpeg), TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(path, bytes[..10], TestContext.Current.CancellationToken);

        var metadata = await _reader.ReadAsync(path, TestContext.Current.CancellationToken);

        metadata.GetValue("ExifTool:Warning").ShouldBe("JPEG format error");
    }

    [Fact]
    public async Task Read_throws_for_a_missing_file()
    {
        await Should.ThrowAsync<ExifToolException>(() =>
            _reader.ReadAsync(_files.PathFor("missing.jpg"), TestContext.Current.CancellationToken));
    }

    private static MetadataTag Tag(ImageMetadata metadata, string key)
    {
        metadata.TryGetTag(TagKey.Parse(key), out var tag).ShouldBeTrue($"{key} should be present");
        return tag;
    }

    public void Dispose() => _files.Dispose();
}
