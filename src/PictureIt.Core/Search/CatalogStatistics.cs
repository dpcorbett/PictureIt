using System.Globalization;

namespace PictureIt.Core.Search;

/// <summary>One bar of a chart: a label, how many photos, and its share of the largest bar (0–1) for drawing.</summary>
public sealed record StatBar(string Label, int Count, double Fraction);

/// <summary>A titled chart.</summary>
public sealed record StatChart(string Title, IReadOnlyList<StatBar> Bars);

/// <summary>Charts describing a set of photos: when they were taken, and with what gear and settings.</summary>
public sealed record CatalogStatistics(int PhotoCount, IReadOnlyList<StatChart> Charts)
{
    private const int TopCount = 10;

    // Common focal-length ranges, from ultra-wide to long telephoto (millimetres, lower bound inclusive).
    private static readonly (double From, string Label)[] FocalRanges =
    [
        (0, "Under 16 mm"), (16, "16–23 mm"), (24, "24–34 mm"), (35, "35–49 mm"), (50, "50–84 mm"),
        (85, "85–134 mm"), (135, "135–199 mm"), (200, "200–399 mm"), (400, "400 mm and over"),
    ];

    public static CatalogStatistics From(IReadOnlyCollection<CatalogEntry> photos, IFormatProvider? culture = null)
    {
        ArgumentNullException.ThrowIfNull(photos);
        culture ??= CultureInfo.CurrentCulture;
        var charts = new List<StatChart>();

        Add(charts, TakenChart(photos, culture));
        Add(charts, Chart("Cameras", Top(photos.Select(p => p.Camera))));
        Add(charts, Chart("Lenses", Top(photos.Select(p => p.Lens))));
        Add(charts, Chart("Focal length", FocalRanges
            .Select((range, i) => (range.Label, Count: photos.Count(p => p.FocalLength is { } mm && mm >= range.From && (i == FocalRanges.Length - 1 || mm < FocalRanges[i + 1].From))))
            .Where(b => b.Count > 0)));
        Add(charts, Chart("Aperture", photos
            .Where(p => p.FNumber is > 0)
            .GroupBy(p => Math.Round(p.FNumber!.Value, 1))
            .OrderBy(g => g.Key)
            .Select(g => ($"f/{g.Key.ToString("0.#", culture)}", g.Count()))));
        Add(charts, Chart("ISO", photos
            .Where(p => p.Iso is > 0)
            .GroupBy(p => p.Iso!.Value)
            .OrderBy(g => g.Key)
            .Select(g => (g.Key.ToString(culture), g.Count()))));
        Add(charts, Chart("Rating", photos
            .GroupBy(p => p.Rating ?? 0)
            .OrderByDescending(g => g.Key)
            .Select(g => (g.Key == 0 ? "Not rated" : new string('★', g.Key), g.Count()))));

        return new CatalogStatistics(photos.Count, charts);
    }

    /// <summary>Photos per month, or per year when they span more than three years.</summary>
    private static StatChart? TakenChart(IReadOnlyCollection<CatalogEntry> photos, IFormatProvider culture)
    {
        var dates = photos.Where(p => p.DateTaken is not null).Select(p => p.DateTaken!.Value).ToList();
        if (dates.Count == 0)
        {
            return null;
        }

        var byYear = (dates.Max() - dates.Min()).TotalDays > 3 * 366;
        return byYear
            ? Chart("Photos per year", dates.GroupBy(d => d.Year).OrderBy(g => g.Key).Select(g => (g.Key.ToString(culture), g.Count())))
            : Chart("Photos per month", dates.GroupBy(d => new DateOnly(d.Year, d.Month, 1)).OrderBy(g => g.Key)
                .Select(g => (g.Key.ToString("MMM yyyy", culture), g.Count())));
    }

    private static List<(string Label, int Count)> Top(IEnumerable<string?> values)
    {
        var groups = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .GroupBy(v => v!.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .Select(g => (Label: g.Key, Count: g.Count()))
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return groups.Count <= TopCount
            ? groups
            : [.. groups.Take(TopCount - 1), ($"{groups.Count - (TopCount - 1)} others", groups.Skip(TopCount - 1).Sum(g => g.Count))];
    }

    private static StatChart? Chart(string title, IEnumerable<(string Label, int Count)> bars)
    {
        var list = bars.ToList();
        if (list.Count == 0)
        {
            return null;
        }

        var max = list.Max(b => b.Count);
        return new StatChart(title, [.. list.Select(b => new StatBar(b.Label, b.Count, (double)b.Count / max))]);
    }

    private static void Add(List<StatChart> charts, StatChart? chart)
    {
        if (chart is not null)
        {
            charts.Add(chart);
        }
    }
}
