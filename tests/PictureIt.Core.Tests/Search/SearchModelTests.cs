using System.Globalization;
using PictureIt.Core.Search;

namespace PictureIt.Core.Tests.Search;

public sealed class SearchModelTests
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Fact]
    public void Maps_a_photo_into_a_catalogue_entry()
    {
        var photo = TestMetadata.Create(
            @"C:\photos\a.jpg",
            ("MWG:DateTimeOriginal", "2024:06:15 14:32:10+01:00", null),
            ("IFD0:Make", "SONY", null),
            ("IFD0:Model", "ILCE-7M3", null),
            ("ExifIFD:FocalLength", "35.0 mm", "35"),
            ("ExifIFD:FNumber", "1.8", "1.8"),
            ("ExifIFD:ExposureTime", "1/250", "0.004"),
            ("ExifIFD:ISO", "800", null),
            ("Composite:GPSLatitude", "51 deg 30' 2.52\" N", "51.5007"),
            ("Composite:GPSLongitude", "0 deg 7' 29.16\" W", "-0.1248"),
            ("MWG:Rating", "4", null),
            ("MWG:Keywords", "London; Night; london", null),
            ("MWG:Description", "Big Ben", null),
            ("MWG:Copyright", "© Dave", null),
            ("File:ImageWidth", "6000", null));

        var entry = CatalogEntry.FromMetadata(photo, 1234, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        entry.DateTaken.ShouldBe(new DateTime(2024, 6, 15, 14, 32, 10));
        entry.Camera.ShouldBe("SONY ILCE-7M3");
        entry.FocalLength.ShouldBe(35);
        entry.FNumber.ShouldBe(1.8);
        entry.ExposureTime.ShouldBe(0.004);
        entry.Iso.ShouldBe(800);
        entry.Latitude.ShouldBe(51.5007);
        entry.Longitude.ShouldBe(-0.1248);
        entry.Rating.ShouldBe(4);
        entry.Keywords.ShouldBe(["London", "Night"]);
        entry.Description.ShouldBe("Big Ben");
        entry.Copyright.ShouldBe("© Dave");
        entry.Width.ShouldBe(6000);
        entry.Length.ShouldBe(1234);
        entry.Folder.ShouldBe(@"C:\photos");
    }

    [Fact]
    public void A_photo_without_metadata_still_has_an_entry()
    {
        var entry = CatalogEntry.FromMetadata(TestMetadata.Create(@"C:\a.png"), 1, DateTime.UnixEpoch);

        entry.DateTaken.ShouldBeNull();
        entry.HasLocation.ShouldBeFalse();
        entry.Keywords.ShouldBeEmpty();
    }

    [Fact]
    public void Describes_a_query_in_words()
    {
        PhotoQuery.All.Describe().ShouldBe("All photos");
        PhotoQuery.All.IsEmpty.ShouldBeTrue();

        var query = PhotoQuery.All with
        {
            Text = "big ben",
            Camera = "Canon EOS R5",
            Keyword = "London",
            TakenFrom = new DateOnly(2024, 1, 1),
            MinRating = 3,
            MinIso = 800,
            MaxFocalLength = 35,
            Copyright = Presence.Missing,
            Location = Presence.Has,
        };

        query.IsEmpty.ShouldBeFalse();
        var text = query.Describe();
        text.ShouldContain("\"big ben\"");
        text.ShouldContain("Canon EOS R5");
        text.ShouldContain("keyword London");
        text.ShouldContain("from ");
        text.ShouldContain("3+ stars");
        text.ShouldContain("ISO 800+");
        text.ShouldContain("up to 35mm");
        text.ShouldContain("no copyright");
        text.ShouldContain("with location");
    }

    [Fact]
    public void Charts_when_with_what_and_how()
    {
        CatalogEntry Photo(int month, string camera, double focal, double f, int iso, int? rating = null) => new()
        {
            FilePath = $@"C:\{Guid.NewGuid():N}.jpg",
            DateTaken = new DateTime(2024, month, 1),
            Camera = camera,
            FocalLength = focal,
            FNumber = f,
            Iso = iso,
            Rating = rating,
        };

        CatalogEntry[] photos =
        [
            Photo(1, "Canon EOS R5", 24, 2.8, 100, 5),
            Photo(1, "Canon EOS R5", 50, 1.8, 100),
            Photo(3, "Nikon Z8", 200, 4, 800, 5),
        ];

        var stats = CatalogStatistics.From(photos, Invariant);

        stats.PhotoCount.ShouldBe(3);
        Bars(stats, "Photos per month").ShouldBe([("Jan 2024", 2), ("Mar 2024", 1)]);
        Bars(stats, "Cameras").ShouldBe([("Canon EOS R5", 2), ("Nikon Z8", 1)]);
        Bars(stats, "Focal length").ShouldBe([("24–34 mm", 1), ("50–84 mm", 1), ("200–399 mm", 1)]);
        Bars(stats, "Aperture").ShouldBe([("f/1.8", 1), ("f/2.8", 1), ("f/4", 1)]);
        Bars(stats, "ISO").ShouldBe([("100", 2), ("800", 1)]);
        Bars(stats, "Rating").ShouldBe([("★★★★★", 2), ("Not rated", 1)]);
        stats.Charts.Single(c => c.Title == "Cameras").Bars[0].Fraction.ShouldBe(1);
        stats.Charts.Single(c => c.Title == "Cameras").Bars[1].Fraction.ShouldBe(0.5);

        // Nothing to chart: no lenses recorded.
        stats.Charts.ShouldNotContain(c => c.Title == "Lenses");
    }

    [Fact]
    public void Long_spans_are_charted_by_year_and_long_lists_are_cut_to_the_top_ten()
    {
        var photos = Enumerable.Range(0, 12)
            .Select(i => new CatalogEntry { FilePath = $@"C:\{i}.jpg", DateTaken = new DateTime(2015 + (i % 6), 1, 1), Lens = $"Lens {i:00}" })
            .ToList();

        var stats = CatalogStatistics.From(photos, Invariant);

        stats.Charts[0].Title.ShouldBe("Photos per year");
        var lenses = Bars(stats, "Lenses");
        lenses.Count.ShouldBe(10);
        lenses[^1].ShouldBe(("3 others", 3));
    }

    private static List<(string Label, int Count)> Bars(CatalogStatistics stats, string title) =>
        [.. stats.Charts.Single(c => c.Title == title).Bars.Select(b => (b.Label, b.Count))];
}
