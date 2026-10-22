using PictureIt.Core.Model;
using PictureIt.Core.Operations;

namespace PictureIt.Core.Tests.Operations;

public class FindReplaceTests
{
    private static readonly ImageMetadata Photo = TestMetadata.Create(
        ("MWG:Description", "Trip to london in 2024"),
        ("MWG:Keywords", "London; Big Ben; London Eye"),
        ("XMP-dc:Description", "Trip to london in 2024"),
        ("IFD0:Make", "Canon"),
        ("ExifIFD:DateTimeOriginal", "2024:06:15 14:32:10"),
        ("File:FileName", "london.jpg"));

    [Fact]
    public void Replaces_in_common_fields_ignoring_case_by_default()
    {
        var edits = FindReplace.EditsFor(Photo, Options("london", "London"));

        edits.ShouldBe([MetadataEdit.Set(TagKey.Parse("MWG:Description"), "Trip to London in 2024")]);
    }

    [Fact]
    public void Replaces_list_items_one_by_one()
    {
        var edits = FindReplace.EditsFor(Photo, Options("London", "Londres", matchCase: true));

        edits.ShouldContain(MetadataEdit.Set(TagKey.Parse("MWG:Keywords"), ["Londres", "Big Ben", "Londres Eye"]));
    }

    [Fact]
    public void Drops_list_items_that_become_empty_and_removes_emptied_tags()
    {
        var edits = FindReplace.EditsFor(Photo, Options("^London$|^Big Ben$|^London Eye$", "", useRegex: true));

        edits.ShouldContain(MetadataEdit.Remove(TagKey.Parse("MWG:Keywords")));
    }

    [Fact]
    public void All_text_scope_covers_component_tags_but_not_dates_or_file_names()
    {
        var keys = FindReplace.EditsFor(Photo, Options("2024", "2025", scope: FindReplaceScope.AllTextTags)).Select(e => e.Key.ToString());

        keys.ShouldBe(["XMP-dc:Description"]);
    }

    [Fact]
    public void Treats_dollar_signs_literally_in_plain_text()
    {
        var edits = FindReplace.EditsFor(Photo, Options("2024", "$1"));

        edits.ShouldBe([MetadataEdit.Set(TagKey.Parse("MWG:Description"), "Trip to london in $1")]);
    }

    [Fact]
    public void Supports_regex_groups()
    {
        var edits = FindReplace.EditsFor(Photo, Options(@"in (\d{4})", "($1)", useRegex: true));

        edits.ShouldBe([MetadataEdit.Set(TagKey.Parse("MWG:Description"), "Trip to london (2024)")]);
    }

    [Theory]
    [InlineData("", false, "Enter the text to find.")]
    [InlineData("(unclosed", true, "The pattern isn't valid")]
    public void Validates_options(string find, bool regex, string message)
    {
        Options(find, "x", useRegex: regex).Validate().ShouldNotBeNull().ShouldStartWith(message);
    }

    private static FindReplaceOptions Options(
        string find,
        string replace,
        bool useRegex = false,
        bool matchCase = false,
        FindReplaceScope scope = FindReplaceScope.CommonFields) =>
        new(find, replace, useRegex, matchCase, scope);
}
