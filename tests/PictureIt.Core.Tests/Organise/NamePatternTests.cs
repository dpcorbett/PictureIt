using System.Globalization;
using PictureIt.Core.Model;
using PictureIt.Core.Organise;

namespace PictureIt.Core.Tests.Organise;

public sealed class NamePatternTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static readonly ImageMetadata Photo = TestMetadata.Create(
        @"C:\photos\IMG_0042.jpg",
        ("ExifIFD:DateTimeOriginal", "2024:06:15 14:32:10", null),
        ("IFD0:Make", "Canon", null),
        ("IFD0:Model", "Canon EOS R5", null),
        ("ExifIFD:LensModel", "RF50mm F1.2 L USM", null),
        ("ExifIFD:FocalLength", "50.0 mm", "50"),
        ("ExifIFD:FNumber", "2.8", "2.8"),
        ("ExifIFD:ISO", "400", null),
        ("MWG:City", "London", null),
        ("MWG:Creator", "Ann Smith; Bob Jones", null));

    private static string Render(string pattern, int sequence = 1) => NamePattern.Parse(pattern).Render(Photo, sequence, Invariant).Name;

    [Theory]
    [InlineData("{Name}", "IMG_0042")]
    [InlineData("{DateTaken}", "2024-06-15")]
    [InlineData("{DateTaken:yyyy-MM-dd HH.mm.ss}", "2024-06-15 14.32.10")]
    [InlineData("{DateTaken:MMMM yyyy}", "June 2024")]
    [InlineData("{Camera}", "Canon EOS R5")]
    [InlineData("{Make} {Model}", "Canon Canon EOS R5")]
    [InlineData("{Lens}", "RF50mm F1.2 L USM")]
    [InlineData("{FocalLength} {Aperture} ISO{ISO}", "50mm f2.8 ISO400")]
    [InlineData("{City}", "London")]
    [InlineData("{Creator}", "Ann Smith")]
    [InlineData("{IFD0:Model}", "Canon EOS R5")]
    [InlineData("{dateTaken:yyyy}", "2024")]
    public void Fills_in_tokens(string pattern, string expected) => Render(pattern).ShouldBe(expected);

    [Theory]
    [InlineData("{Seq}", 7, "7")]
    [InlineData("{Seq:000}", 7, "007")]
    [InlineData("{Seq:00}", 123, "123")]
    public void Numbers_in_sequence(string pattern, int sequence, string expected) => Render(pattern, sequence).ShouldBe(expected);

    [Fact]
    public void Adds_the_make_when_the_model_lacks_it()
    {
        var sony = TestMetadata.Create(("IFD0:Make", "SONY"), ("IFD0:Model", "ILCE-7M3"));
        NamePattern.Parse("{Camera}").Render(sony, 1, Invariant).Name.ShouldBe("SONY ILCE-7M3");
    }

    [Fact]
    public void Missing_values_are_left_out_and_reported()
    {
        var result = NamePattern.Parse("{Country} {City}").Render(Photo, 1, Invariant);

        result.Name.ShouldBe("London");
        result.Missing.ShouldBe(["Country"]);
    }

    [Fact]
    public void Missing_values_use_the_fallback()
    {
        var result = NamePattern.Parse("{Country|Unknown}_{Name}").Render(Photo, 1, Invariant);

        result.Name.ShouldBe("Unknown_IMG_0042");
        result.Missing.ShouldBe(["Country"]);
    }

    [Fact]
    public void Characters_Windows_rejects_are_replaced()
    {
        var photo = TestMetadata.Create(@"C:\a.jpg", ("XMP-dc:Title", "Who? Me: \"yes\" <no>", null));
        NamePattern.Parse("{Title}").Render(photo, 1, Invariant).Name.ShouldBe("Who_ Me_ _yes_ _no_");
    }

    [Fact]
    public void A_value_cannot_add_folders()
    {
        var photo = TestMetadata.Create(@"C:\a.jpg", ("XMP-dc:Title", @"a\b/c", null));
        NamePattern.Parse(@"{Title}\x").Render(photo, 1, Invariant).Name.ShouldBe(@"a_b_c\x");
    }

    [Fact]
    public void Folder_patterns_drop_empty_folders_and_trailing_dots()
    {
        var undated = TestMetadata.Create(@"C:\a.jpg", ("IFD0:Model", "X. ", null));
        NamePattern.Parse(@"{DateTaken:yyyy}/{Model}\done").Render(undated, 1, Invariant).Name.ShouldBe(@"X\done");
    }

    [Fact]
    public void Reserved_device_names_are_made_safe()
    {
        var photo = TestMetadata.Create(@"C:\a.jpg", ("XMP-dc:Title", "con", null));
        NamePattern.Parse("{Title}").Render(photo, 1, Invariant).Name.ShouldBe("_con");
    }

    [Fact]
    public void Double_braces_are_literal()
    {
        Render("{{x}} {Name}").ShouldBe("{x} IMG_0042");
    }

    [Fact]
    public void Describes_what_the_pattern_uses()
    {
        var pattern = NamePattern.Parse(@"{DateTaken:yyyy}\{Name}_{Seq}");

        pattern.HasFolders.ShouldBeTrue();
        pattern.UsesSequence.ShouldBeTrue();
        pattern.UsesOriginalName.ShouldBeTrue();
        NamePattern.Parse("{Camera}").UsesSequence.ShouldBeFalse();
    }

    [Theory]
    [InlineData("", "Enter a pattern.")]
    [InlineData("{Name", "{ without a matching }")]
    [InlineData("Name}", "} without a matching {")]
    [InlineData("{}", "empty {}")]
    [InlineData("{Nope}", "{Nope} isn't a token or a tag")]
    [InlineData("{Camera:x}", "{Camera} doesn't take a format")]
    [InlineData("{Seq:##}", "zeros for the digits")]
    [InlineData("{DateTaken:%}", "isn't a date format")]
    public void Rejects_bad_patterns(string text, string message)
    {
        NamePattern.TryParse(text, out _, out var error).ShouldBeFalse();
        error.ShouldNotBeNull().ShouldContain(message);
        Should.Throw<FormatException>(() => NamePattern.Parse(text));
    }

    [Fact]
    public void Lists_every_token_with_an_example_that_parses()
    {
        foreach (var token in NamePattern.Tokens)
        {
            NamePattern.TryParse(token.Example, out _, out var error).ShouldBeTrue(error);
        }
    }
}
