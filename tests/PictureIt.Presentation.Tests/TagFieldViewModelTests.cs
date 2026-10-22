using PictureIt.Core.Catalog;
using PictureIt.Core.Model;
using PictureIt.Presentation.ViewModels;

namespace PictureIt.Presentation.Tests;

public sealed class TagFieldViewModelTests
{
    private static readonly TagKey Key = TagKey.Parse("XMP-dc:Test");

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

        field.ToEdits().ShouldBe([MetadataEdit.Set(Key, "Line one\r\nLine two\r\n")]);
    }

    [Fact]
    public void An_emptied_list_becomes_a_removal()
    {
        var field = Create(EditorKind.List, "a; b");
        field.Value = " ; ";

        field.ToEdits().ShouldBe([MetadataEdit.Remove(Key)]);
    }

    [Fact]
    public void Typing_into_a_field_marked_for_clearing_cancels_the_clear()
    {
        var field = Create(EditorKind.Text, string.Empty, mixed: true);
        field.ClearAllCommand.Execute(null);

        field.Value = "New";

        field.ClearRequested.ShouldBeFalse();
        field.ToEdits().ShouldBe([MetadataEdit.Set(Key, "New")]);
    }

    [Fact]
    public void Clear_all_is_only_offered_for_mixed_fields()
    {
        Create(EditorKind.Text, "x").ClearAllCommand.CanExecute(null).ShouldBeFalse();
        Create(EditorKind.Text, string.Empty, mixed: true).ClearAllCommand.CanExecute(null).ShouldBeTrue();
    }

    private static TagFieldViewModel Create(EditorKind editor, string value, Action? changed = null, bool readOnly = false, bool mixed = false) =>
        new(Key, "Test", TagCategory.Other, editor, value, readOnly, changed ?? (() => { }), mixed);
}
