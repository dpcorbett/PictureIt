using PictureIt.Infrastructure.Summary;

namespace PictureIt.Infrastructure.Tests.Summary;

public sealed class MetadataExtractorSummaryReaderTests : IDisposable
{
    private readonly TestFiles _files = new();
    private readonly MetadataExtractorSummaryReader _reader = new();

    [Fact]
    public void Reads_the_key_fields_of_a_jpeg()
    {
        var summary = _reader.ReadSummary(_files.Copy(TestFiles.SampleJpeg)).ShouldNotBeNull();

        summary.CameraMake.ShouldBe("Canon");
        summary.CameraModel.ShouldBe("Canon EOS R5");
        summary.LensModel.ShouldBe("RF50mm F1.8 STM");
        summary.DateTaken.ShouldBe(new DateTime(2024, 6, 15, 14, 32, 10));
        summary.Width.ShouldBe(96);
        summary.Height.ShouldBe(64);
        summary.ExposureTime!.Value.ShouldBe(0.004, tolerance: 1e-9);
        summary.FNumber!.Value.ShouldBe(2.8, tolerance: 1e-9);
        summary.Iso.ShouldBe(400);
        summary.FocalLength!.Value.ShouldBe(50, tolerance: 1e-9);
        summary.Latitude!.Value.ShouldBe(51.5007, tolerance: 1e-4);
        summary.Longitude!.Value.ShouldBe(-0.1246, tolerance: 1e-4);
        summary.HasLocation.ShouldBeTrue();
    }

    [Fact]
    public void Reads_dimensions_of_a_png_without_camera_metadata()
    {
        var summary = _reader.ReadSummary(_files.Copy(TestFiles.SamplePng)).ShouldNotBeNull();

        summary.Width.ShouldBe(96);
        summary.Height.ShouldBe(64);
        summary.CameraModel.ShouldBeNull();
        summary.HasLocation.ShouldBeFalse();
    }

    [Fact]
    public void Returns_null_for_a_file_that_is_not_an_image()
    {
        var path = _files.PathFor("notes.txt");
        File.WriteAllText(path, "hello");

        _reader.ReadSummary(path).ShouldBeNull();
    }

    public void Dispose() => _files.Dispose();
}
