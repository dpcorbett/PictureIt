using System.Globalization;

namespace PictureIt.Core.Search;

/// <summary>A yes/no/either filter.</summary>
public enum Presence
{
    Any,
    Has,
    Missing,
}

/// <summary>
/// A search of the catalogue. Every condition that's set must match; unset ones are ignored.
/// </summary>
public sealed record PhotoQuery
{
    /// <summary>Words to find in the title, description, keywords, place, creator or file name. All must match (as word prefixes).</summary>
    public string? Text { get; init; }

    /// <summary>Only photos in this folder or below it.</summary>
    public string? Folder { get; init; }

    public string? Camera { get; init; }

    public string? Lens { get; init; }

    public string? Keyword { get; init; }

    /// <summary>Taken on or after this day.</summary>
    public DateOnly? TakenFrom { get; init; }

    /// <summary>Taken on or before this day.</summary>
    public DateOnly? TakenTo { get; init; }

    public int? MinRating { get; init; }

    public double? MinFocalLength { get; init; }

    public double? MaxFocalLength { get; init; }

    public double? MinFNumber { get; init; }

    public double? MaxFNumber { get; init; }

    public int? MinIso { get; init; }

    public int? MaxIso { get; init; }

    public Presence Location { get; init; }

    public Presence DateTaken { get; init; }

    public Presence Copyright { get; init; }

    public Presence Keywords { get; init; }

    public static PhotoQuery All { get; } = new();

    public bool IsEmpty => this == All;

    /// <summary>A short summary, e.g. "Canon EOS R5 · 2024 · no copyright".</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Text))
        {
            parts.Add($"\"{Text.Trim()}\"");
        }

        AddIf(Camera);
        AddIf(Lens);
        if (!string.IsNullOrWhiteSpace(Keyword))
        {
            parts.Add($"keyword {Keyword}");
        }

        if (TakenFrom is not null || TakenTo is not null)
        {
            parts.Add((TakenFrom, TakenTo) switch
            {
                ({ } from, { } to) when from == to => Day(from),
                ({ } from, { } to) => $"{Day(from)} to {Day(to)}",
                ({ } from, null) => $"from {Day(from)}",
                (null, { } to) => $"until {Day(to)}",
                _ => string.Empty,
            });
        }

        if (MinRating is > 0)
        {
            parts.Add($"{MinRating}+ stars");
        }

        AddRange("mm", MinFocalLength, MaxFocalLength);
        AddRange("f/", MinFNumber, MaxFNumber, prefix: true);
        AddRange("ISO ", MinIso, MaxIso, prefix: true);
        AddPresence(Location, "with location", "no location");
        AddPresence(DateTaken, "with date taken", "no date taken");
        AddPresence(Copyright, "with copyright", "no copyright");
        AddPresence(Keywords, "with keywords", "no keywords");
        if (!string.IsNullOrWhiteSpace(Folder))
        {
            parts.Add($"in {Path.GetFileName(Path.TrimEndingDirectorySeparator(Folder))}");
        }

        return parts.Count == 0 ? "All photos" : string.Join(" · ", parts);

        void AddIf(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add(value.Trim());
            }
        }

        void AddPresence(Presence presence, string has, string missing)
        {
            if (presence != Presence.Any)
            {
                parts.Add(presence == Presence.Has ? has : missing);
            }
        }

        void AddRange(string unit, double? min, double? max, bool prefix = false)
        {
            string Text(double v) => prefix ? $"{unit}{v.ToString("0.#", CultureInfo.CurrentCulture)}" : $"{v.ToString("0.#", CultureInfo.CurrentCulture)}{unit}";
            if (min is not null || max is not null)
            {
                parts.Add((min, max) switch
                {
                    ({ } a, { } b) when a == b => Text(a),
                    ({ } a, { } b) => $"{Text(a)}–{Text(b)}",
                    ({ } a, null) => $"{Text(a)}+",
                    (null, { } b) => $"up to {Text(b)}",
                    _ => string.Empty,
                });
            }
        }

        static string Day(DateOnly day) => day.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
    }
}

/// <summary>A search saved under a name, e.g. "No copyright".</summary>
public sealed record SavedSearch(string Name, PhotoQuery Query);

public interface ISavedSearchStore
{
    IReadOnlyList<SavedSearch> Load();

    void Save(IReadOnlyList<SavedSearch> searches);
}
