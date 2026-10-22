using PictureIt.Core.Model;

namespace PictureIt.Core.Tests.Model;

public class ChangePreviewTests
{
    private const string Path = @"C:\photos\a.jpg";
    private static readonly TagKey Title = TagKey.Parse("XMP-dc:Title");
    private static readonly TagKey Keywords = TagKey.Parse("MWG:Keywords");

    private static readonly ImageMetadata Current = new(Path,
    [
        new MetadataTag(Title, "Title", ["Old title"], null, IsList: false, IsWritable: true),
        new MetadataTag(Keywords, "Keywords", ["London", "Big Ben"], null, IsList: true, IsWritable: true),
    ]);

    [Fact]
    public void Shows_old_and_new_values()
    {
        var preview = Preview(MetadataEdit.Set(Title, "New title"));

        preview.Files.ShouldHaveSingleItem().Changes.ShouldBe([new TagChange(Title, "Old title", "New title")]);
        preview.ChangeCount.ShouldBe(1);
    }

    [Fact]
    public void Shows_new_tags_and_removed_tags()
    {
        var artist = TagKey.Parse("IFD0:Artist");

        var changes = Preview(MetadataEdit.Set(artist, "Dave"), MetadataEdit.Remove(Title)).Files[0].Changes;

        changes.ShouldContain(new TagChange(artist, null, "Dave"));
        changes.ShouldContain(new TagChange(Title, "Old title", null));
    }

    [Fact]
    public void Works_out_list_additions_and_removals()
    {
        var changes = Preview(MetadataEdit.AddToList(Keywords, "Thames", "London"), MetadataEdit.RemoveFromList(Keywords, "Big Ben"))
            .Files[0].Changes;

        changes.ShouldBe([new TagChange(Keywords, "London; Big Ben", "London; Thames")]);
    }

    [Fact]
    public void Adding_an_item_that_exists_changes_nothing()
    {
        Preview(MetadataEdit.AddToList(Keywords, "London")).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Leaves_out_edits_that_change_nothing()
    {
        var preview = Preview(MetadataEdit.Set(Title, "Old title"), MetadataEdit.Remove(TagKey.Parse("IFD0:Artist")));

        preview.IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Treats_setting_an_empty_value_as_removal()
    {
        Preview(MetadataEdit.Set(Title, string.Empty)).Files[0].Changes.ShouldBe([new TagChange(Title, "Old title", null)]);
    }

    [Fact]
    public void Treats_files_without_metadata_as_empty()
    {
        var editSet = EditSet.Create("test", [new FileEdits(@"C:\photos\other.jpg", [MetadataEdit.Set(Title, "T")])]);

        var preview = ChangePreview.Create(editSet, new Dictionary<string, ImageMetadata>());

        preview.Files[0].Changes.ShouldBe([new TagChange(Title, null, "T")]);
    }

    private static ChangePreview Preview(params MetadataEdit[] edits) =>
        ChangePreview.Create(
            EditSet.Create("test", [new FileEdits(Path, edits)]),
            new Dictionary<string, ImageMetadata> { [Path] = Current });
}
