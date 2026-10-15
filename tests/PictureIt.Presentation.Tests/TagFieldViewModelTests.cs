using PictureIt.Core.Catalog;
using PictureIt.Core.Model;
using PictureIt.Presentation.ViewModels;

namespace PictureIt.Presentation.Tests;

public sealed class TagFieldViewModelTests
{
    [Fact]
    public void Loading_a_value_is_not_an_edit()
    {
        var changes = 0;
        var field = Create(EditorKind.Text, "x", () => changes++);

        field.IsDirty.ShouldBeFalse();
        changes.ShouldBe(0);
    }

    [Fact]
    public void Read_only_fields_are_never_dirty()
    {
        var field = Create(EditorKind.Text, "x", readOnly: true);

        field.Value = "y";

        field.IsDirty.ShouldBeFalse();
    }

    [Theory]
    [InlineData(-1, "")]
    [InlineData(0, "0")]
    [InlineData(4, "4")]
    public void Rating_maps_stars_to_text(double stars, string expected)
    {
        var field = Create(EditorKind.Rating, "3");

        field.RatingValue = stars;

        field.Value.ShouldBe(expected);
    }

    [Fact]
    public void Unset_rating_shows_as_no_stars()
    {
        Create(EditorKind.Rating, string.Empty).RatingValue.ShouldBe(-1);
    }

    [Fact]
    public void Multiline_values_keep_surrounding_whitespace()
    {
        var field = Create(EditorKind.MultilineText, string.Empty);
        field.Value = "Line one\r\nLine two\r\n";

        field.ToEdit().Values.ShouldBe(["Line one\r\nLine two\r\n"]);
    }

    [Fact]
    public void An_emptied_list_becomes_a_removal()
    {
        var field = Create(EditorKind.List, "a; b");
        field.Value = " ; ";

        field.ToEdit().Kind.ShouldBe(EditKind.Remove);
    }

    private static TagFieldViewModel Create(EditorKind editor, string value, Action? changed = null, bool readOnly = false) =>
        new(TagKey.Parse("XMP-dc:Test"), "Test", TagCategory.Other, editor, value, readOnly, changed ?? (() => { }));
}
