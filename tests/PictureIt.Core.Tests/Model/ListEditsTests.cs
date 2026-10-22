using PictureIt.Core.Model;

namespace PictureIt.Core.Tests.Model;

public class ListEditsTests
{
    private static readonly TagKey Keywords = TagKey.Parse("MWG:Keywords");
    private static readonly TagKey Title = TagKey.Parse("XMP-dc:Title");

    [Fact]
    public void Add_keeps_existing_order_and_skips_duplicates()
    {
        ListEdits.Apply(["a", "b"], MetadataEdit.AddToList(Keywords, "b", "c", "c")).ShouldBe(["a", "b", "c"]);
    }

    [Fact]
    public void Add_to_a_missing_tag_creates_it()
    {
        ListEdits.Apply(null, MetadataEdit.AddToList(Keywords, "a")).ShouldBe(["a"]);
    }

    [Fact]
    public void Removing_the_last_item_removes_the_tag()
    {
        ListEdits.Apply(["a"], MetadataEdit.RemoveFromList(Keywords, "a")).ShouldBeNull();
    }

    [Fact]
    public void Set_drops_blank_values()
    {
        ListEdits.Apply(["x"], MetadataEdit.Set(Keywords, ["a", "", "b"])).ShouldBe(["a", "b"]);
        ListEdits.Apply(["x"], MetadataEdit.Set(Keywords, "")).ShouldBeNull();
    }

    [Fact]
    public void Resolve_collapses_list_edits_into_one_set_in_place()
    {
        var resolved = ListEdits.Resolve(
            [
                MetadataEdit.Set(Title, "T"),
                MetadataEdit.AddToList(Keywords, "c"),
                MetadataEdit.RemoveFromList(Keywords, "a"),
            ],
            key => key == Keywords ? ["a", "b"] : null);

        resolved.Count.ShouldBe(2);
        resolved[0].ShouldBe(MetadataEdit.Set(Title, "T"), new MetadataEditComparer());
        resolved[1].Kind.ShouldBe(EditKind.Set);
        resolved[1].Values.ShouldBe(["b", "c"]);
    }

    [Fact]
    public void Resolve_turns_an_emptied_list_into_a_removal()
    {
        var resolved = ListEdits.Resolve([MetadataEdit.RemoveFromList(Keywords, "a")], _ => ["a"]);

        resolved.ShouldHaveSingleItem().Kind.ShouldBe(EditKind.Remove);
    }

    [Fact]
    public void Resolve_leaves_edits_without_list_operations_alone()
    {
        IReadOnlyList<MetadataEdit> edits = [MetadataEdit.Set(Title, "T")];

        ListEdits.Resolve(edits, _ => throw new InvalidOperationException("should not read")).ShouldBeSameAs(edits);
    }

    private sealed class MetadataEditComparer : IEqualityComparer<MetadataEdit>
    {
        public bool Equals(MetadataEdit? x, MetadataEdit? y) =>
            x is not null && y is not null && x.Key == y.Key && x.Kind == y.Kind && x.Values.SequenceEqual(y.Values);

        public int GetHashCode(MetadataEdit obj) => obj.Key.GetHashCode();
    }
}
