using PictureIt.Infrastructure.ExifTool;

namespace PictureIt.Infrastructure.Tests.ExifTool;

public sealed class ExifToolWriterInterpretTests
{
    [Fact]
    public void Updated_is_success()
    {
        var result = ExifToolWriter.Interpret("a.jpg", new("    1 image files updated\n", ""));

        result.Succeeded.ShouldBeTrue();
        result.Error.ShouldBeNull();
    }

    [Fact]
    public void Unchanged_without_warnings_is_success()
    {
        ExifToolWriter.Interpret("a.jpg", new("    1 image files unchanged\n", "")).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Unchanged_with_a_warning_is_a_failure_carrying_the_warning()
    {
        var result = ExifToolWriter.Interpret(
            "a.jpg",
            new("    1 image files unchanged\n", "Warning: Tag 'XMP-dc:Nope' is not defined\n"));

        result.Succeeded.ShouldBeFalse();
        result.Error.ShouldBe("Warning: Tag 'XMP-dc:Nope' is not defined");
    }

    [Fact]
    public void Updated_with_a_warning_is_success_and_keeps_the_warning()
    {
        var result = ExifToolWriter.Interpret(
            "a.jpg",
            new("    1 image files updated\n", "Warning: [minor] Fixed incorrect URI\n"));

        result.Succeeded.ShouldBeTrue();
        result.Warnings.ShouldBe(["Warning: [minor] Fixed incorrect URI"]);
    }

    [Fact]
    public void An_error_is_a_failure()
    {
        var result = ExifToolWriter.Interpret(
            "a.jpg",
            new("    0 image files updated\n    1 files weren't updated due to errors\n", "Error: Not a valid JPG - a.jpg\n"));

        result.Succeeded.ShouldBeFalse();
        result.Error.ShouldBe("Error: Not a valid JPG - a.jpg");
    }
}
