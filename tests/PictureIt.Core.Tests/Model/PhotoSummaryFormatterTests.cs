using PictureIt.Core.Model;

namespace PictureIt.Core.Tests.Model;

public class PhotoSummaryFormatterTests
{
    [Fact]
    public void Describes_a_full_summary()
    {
        var summary = new PhotoSummary
        {
            CameraMake = "Canon",
            CameraModel = "Canon EOS R5",
            LensModel = "RF50mm F1.8 STM",
            ExposureTime = 0.004,
            FNumber = 2.8,
            Iso = 400,
            FocalLength = 50,
            Width = 8192,
            Height = 5464,
        };

        PhotoSummaryFormatter.Describe(summary)
            .ShouldBe("Canon EOS R5 · RF50mm F1.8 STM · 1/250 s · f/2.8 · ISO 400 · 50 mm · 8192 × 5464");
    }

    [Fact]
    public void Describes_an_empty_summary_as_empty()
    {
        PhotoSummaryFormatter.Describe(new PhotoSummary()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Canon", "Canon EOS R5", "Canon EOS R5")]
    [InlineData("NIKON CORPORATION", "NIKON Z 6", "NIKON Z 6")]
    [InlineData("FUJIFILM", "X-T5", "FUJIFILM X-T5")]
    [InlineData(null, "X-T5", "X-T5")]
    [InlineData("FUJIFILM", null, "FUJIFILM")]
    [InlineData(null, null, null)]
    public void Avoids_repeating_the_make(string? make, string? model, string? expected)
    {
        PhotoSummaryFormatter.Camera(new PhotoSummary { CameraMake = make, CameraModel = model }).ShouldBe(expected);
    }

    [Theory]
    [InlineData(0.004, "1/250 s")]
    [InlineData(0.0125, "1/80 s")]
    [InlineData(1, "1 s")]
    [InlineData(2.5, "2.5 s")]
    public void Formats_exposure_times(double seconds, string expected)
    {
        PhotoSummaryFormatter.Exposure(seconds).ShouldBe(expected);
    }
}
