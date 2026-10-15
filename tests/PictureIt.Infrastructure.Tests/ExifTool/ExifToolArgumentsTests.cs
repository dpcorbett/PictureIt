using PictureIt.Core.Model;
using PictureIt.Infrastructure.ExifTool;

namespace PictureIt.Infrastructure.Tests.ExifTool;

public sealed class ExifToolArgumentsTests : IDisposable
{
    private static readonly TagKey Subject = TagKey.Parse("XMP-dc:Subject");

    private readonly TestFiles _files = new();

    [Fact]
    public void Maps_each_edit_kind_to_an_assignment()
    {
        var args = ExifToolArguments.ForEdits(
            [
                MetadataEdit.Set(TagKey.Parse("IFD0:Artist"), "Dave"),
                MetadataEdit.Set(Subject, ["a", "b"]),
                MetadataEdit.AddToList(Subject, "c"),
                MetadataEdit.RemoveFromList(Subject, "d"),
                MetadataEdit.Remove(TagKey.Parse("ExifIFD:SerialNumber")),
            ],
            _files.Directory);

        args.ShouldBe(
        [
            "-IFD0:Artist=Dave",
            "-XMP-dc:Subject=a",
            "-XMP-dc:Subject=b",
            "-XMP-dc:Subject+=c",
            "-XMP-dc:Subject-=d",
            "-ExifIFD:SerialNumber=",
        ]);
    }

    [Theory]
    [InlineData("two\nlines", true)]
    [InlineData("carriage\rreturn", true)]
    [InlineData(" leading", true)]
    [InlineData("trailing ", true)]
    [InlineData("inner spaces are fine", false)]
    [InlineData(@"back\slash", false)]
    [InlineData("", false)]
    public void Routes_only_values_that_cannot_survive_a_line_through_a_file(string value, bool expected)
    {
        ExifToolArguments.NeedsValueFile(value).ShouldBe(expected);
    }

    [Fact]
    public void Writes_multi_line_values_to_a_file_assignment()
    {
        var args = ExifToolArguments.ForEdits([MetadataEdit.AddToList(Subject, "two\nlines")], _files.Directory);

        var arg = args.ShouldHaveSingleItem();
        arg.ShouldStartWith("-XMP-dc:Subject+<=");
        File.ReadAllText(arg["-XMP-dc:Subject+<=".Length..]).ShouldBe("two\nlines");
    }

    public void Dispose() => _files.Dispose();
}
