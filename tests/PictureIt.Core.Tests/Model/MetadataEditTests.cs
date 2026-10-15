using PictureIt.Core.Model;

namespace PictureIt.Core.Tests.Model;

public class MetadataEditTests
{
    private static readonly TagKey Subject = TagKey.Parse("XMP-dc:Subject");

    [Fact]
    public void Set_with_one_value_holds_that_value()
    {
        var edit = MetadataEdit.Set(Subject, "London");

        edit.Kind.ShouldBe(EditKind.Set);
        edit.Values.ShouldBe(["London"]);
    }

    [Fact]
    public void Set_with_a_list_holds_every_value()
    {
        MetadataEdit.Set(Subject, ["a", "b"]).Values.ShouldBe(["a", "b"]);
    }

    [Fact]
    public void Remove_has_no_values()
    {
        var edit = MetadataEdit.Remove(Subject);

        edit.Kind.ShouldBe(EditKind.Remove);
        edit.Values.ShouldBeEmpty();
    }

    [Fact]
    public void List_edits_require_at_least_one_value()
    {
        Should.Throw<ArgumentException>(() => MetadataEdit.AddToList(Subject));
        Should.Throw<ArgumentException>(() => MetadataEdit.RemoveFromList(Subject));
        Should.Throw<ArgumentException>(() => MetadataEdit.Set(Subject, Array.Empty<string>()));
    }

    [Fact]
    public void EditSet_create_assigns_a_new_batch_id()
    {
        var files = new[] { new FileEdits("a.jpg", [MetadataEdit.Remove(Subject)]) };

        var first = EditSet.Create("one", files);
        var second = EditSet.Create("two", files);

        first.BatchId.ShouldNotBe(Guid.Empty);
        first.BatchId.ShouldNotBe(second.BatchId);
        first.Files.ShouldBe(files);
    }
}
