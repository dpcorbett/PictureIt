using System.Globalization;
using PictureIt.Core.Model;
using PictureIt.Core.Organise;

namespace PictureIt.Core.Tests.Organise;

public sealed class FilePlannerTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static ImageMetadata Photo(string name, string? taken = null) =>
        taken is null
            ? TestMetadata.Create($@"C:\photos\{name}", ("IFD0:Model", "Canon EOS R5", null))
            : TestMetadata.Create($@"C:\photos\{name}", ("IFD0:Model", "Canon EOS R5", null), ("ExifIFD:DateTimeOriginal", taken, null));

    private static FilePlan Rename(IReadOnlyList<ImageMetadata> photos, string pattern, bool byDate = false, Func<string, bool>? exists = null, int first = 1) =>
        FilePlanner.Rename(photos, new RenameOptions(NamePattern.Parse(pattern), byDate, first), exists ?? (_ => false), Invariant);

    private static IEnumerable<string> Targets(FilePlan plan) => plan.Operations.Select(o => Path.GetFileName(o.TargetPath));

    [Fact]
    public void Renames_keeping_the_folder_and_extension()
    {
        var plan = Rename([Photo("IMG_1.JPG", "2024:06:15 14:32:10")], "{DateTaken:yyyy-MM-dd}");

        plan.Kind.ShouldBe(FileOperationKind.Rename);
        plan.Operations.Single().TargetPath.ShouldBe(@"C:\photos\2024-06-15.JPG");
        plan.ChangeCount.ShouldBe(1);
        plan.Description.ShouldBe("Rename 1 photo to \"{DateTaken:yyyy-MM-dd}\"");
    }

    [Fact]
    public void Numbers_in_the_given_order_or_by_date_taken()
    {
        var photos = new[] { Photo("b.jpg", "2024:06:15 12:00:00"), Photo("a.jpg", "2024:06:15 09:00:00"), Photo("c.jpg") };

        Targets(Rename(photos, "{Seq:00}")).ShouldBe(["01.jpg", "02.jpg", "03.jpg"]);

        // By date: a (09:00), b (12:00), then c (no date) last.
        var byDate = Rename(photos, "{Seq:00}", byDate: true);
        byDate.Operations.Select(o => (Path.GetFileName(o.SourcePath), Path.GetFileName(o.TargetPath)))
            .ShouldBe([("a.jpg", "01.jpg"), ("b.jpg", "02.jpg"), ("c.jpg", "03.jpg")]);
    }

    [Fact]
    public void Starts_numbering_where_asked()
    {
        Targets(Rename([Photo("a.jpg"), Photo("b.jpg")], "{Seq:000}", first: 41)).ShouldBe(["041.jpg", "042.jpg"]);
    }

    [Fact]
    public void Clashing_names_get_a_number()
    {
        var plan = Rename([Photo("a.jpg"), Photo("b.jpg"), Photo("c.jpg")], "{Camera}");

        Targets(plan).ShouldBe(["Canon EOS R5.jpg", "Canon EOS R5 (2).jpg", "Canon EOS R5 (3).jpg"]);
        plan.Operations[1].Note.ShouldNotBeNull().ShouldContain("Added (2)");
    }

    [Fact]
    public void Never_targets_an_existing_file_outside_the_batch()
    {
        var plan = Rename([Photo("a.jpg")], "{Camera}", exists: p => p.EndsWith(@"\Canon EOS R5.jpg", StringComparison.Ordinal));

        Targets(plan).ShouldBe(["Canon EOS R5 (2).jpg"]);
    }

    [Fact]
    public void Names_freed_by_the_batch_can_be_reused()
    {
        // b → a while a → b: both names exist on disk now, but both files are moving.
        var photos = new[] { Photo("a.jpg"), Photo("b.jpg") };
        var plan = Rename(photos, "{Seq}", exists: p => p.EndsWith(@"\1.jpg", StringComparison.Ordinal) || p.EndsWith(@"\2.jpg", StringComparison.Ordinal));

        // 1.jpg and 2.jpg exist but aren't batch photos, so they're in the way.
        Targets(plan).ShouldBe(["1 (2).jpg", "2 (2).jpg"]);

        // 1 → 2 and 2 → 3: 2.jpg exists, but it's moving out of the way.
        var shift = FilePlanner.Rename(
            [Photo("1.jpg"), Photo("2.jpg")],
            new RenameOptions(NamePattern.Parse("{Seq}"), FirstNumber: 2),
            p => p.EndsWith(@".jpg", StringComparison.Ordinal) || p.EndsWith(@".jpg", StringComparison.Ordinal),
            Invariant);
        Targets(shift).ShouldBe(["2.jpg", "3.jpg"]);
    }

    [Fact]
    public void A_photo_that_already_has_the_name_is_unchanged_and_keeps_it()
    {
        var plan = Rename([Photo("Canon EOS R5.jpg"), Photo("x.jpg")], "{Camera}", exists: p => p.EndsWith(@"\Canon EOS R5.jpg", StringComparison.Ordinal));

        plan.Operations[0].IsUnchanged.ShouldBeTrue();
        Targets(plan).ShouldBe(["Canon EOS R5.jpg", "Canon EOS R5 (2).jpg"]);
        plan.ChangeCount.ShouldBe(1);
    }

    [Fact]
    public void A_change_of_case_is_a_change()
    {
        var plan = Rename([Photo("IMG_1.jpg")], "img_1", exists: p => p.Equals(@"C:\photos\IMG_1.jpg", StringComparison.OrdinalIgnoreCase));

        plan.Operations.Single().TargetPath.ShouldBe(@"C:\photos\img_1.jpg");
        plan.Operations.Single().IsUnchanged.ShouldBeFalse();
    }

    [Fact]
    public void Notes_missing_values_and_keeps_empty_names_as_they_are()
    {
        var plan = Rename([Photo("a.jpg")], "{City}");

        plan.Operations.Single().IsUnchanged.ShouldBeTrue();
        plan.Operations.Single().Note.ShouldNotBeNull().ShouldContain("empty");

        Rename([Photo("a.jpg")], "{City}_{Name}").Operations.Single().Note.ShouldBe("No city.");
    }

    [Fact]
    public void Rename_patterns_cannot_have_folders()
    {
        Should.Throw<ArgumentException>(() => Rename([Photo("a.jpg")], @"x\{Name}"));
    }

    [Fact]
    public void Sorts_into_folders_under_the_destination()
    {
        var photos = new[] { Photo("a.jpg", "2024:06:15 09:00:00"), Photo("b.jpg") };
        var plan = FilePlanner.Sort(photos, new SortOptions(NamePattern.Parse(@"{DateTaken:yyyy|Undated}\{DateTaken:MM}"), @"D:\Sorted", Copy: false), _ => false, Invariant);

        plan.Kind.ShouldBe(FileOperationKind.Move);
        plan.Operations.Select(o => o.TargetPath).ShouldBe([@"D:\Sorted\2024\06\a.jpg", @"D:\Sorted\Undated\b.jpg"]);
        plan.Operations[1].Note.ShouldBe("No date taken.");
    }

    [Fact]
    public void Photos_already_in_place_are_unchanged()
    {
        var plan = FilePlanner.Sort([Photo("a.jpg")], new SortOptions(NamePattern.Parse("PHOTOS"), @"C:\", Copy: false), p => p == @"C:\photos\a.jpg", Invariant);

        plan.Operations.Single().IsUnchanged.ShouldBeTrue();
    }

    [Fact]
    public void Copies_never_clash_with_their_originals()
    {
        // Copying into the same folder: the original stays, so the copy gets a number.
        var plan = FilePlanner.Sort([Photo("a.jpg")], new SortOptions(NamePattern.Parse("{City}"), @"C:\photos", Copy: true), p => p == @"C:\photos\a.jpg", Invariant);

        plan.Kind.ShouldBe(FileOperationKind.Copy);
        plan.Operations.Single().TargetPath.ShouldBe(@"C:\photos\a (2).jpg");
    }

    [Fact]
    public void Same_names_from_different_folders_get_numbers()
    {
        var photos = new[]
        {
            TestMetadata.Create(@"C:\one\a.jpg", ("IFD0:Model", "X", null)),
            TestMetadata.Create(@"C:\two\a.jpg", ("IFD0:Model", "X", null)),
        };
        var plan = FilePlanner.Sort(photos, new SortOptions(NamePattern.Parse("{Model}"), @"D:\out", Copy: true), _ => false, Invariant);

        plan.Operations.Select(o => o.TargetPath).ShouldBe([@"D:\out\X\a.jpg", @"D:\out\X\a (2).jpg"]);
    }

    [Fact]
    public void Gives_up_rather_than_looping_when_every_name_is_taken()
    {
        Should.Throw<InvalidOperationException>(() => Rename([Photo("a.jpg")], "x", exists: _ => true));
    }
}
