using System.Globalization;
using System.Text.RegularExpressions;

namespace PictureIt.Core.Operations;

/// <summary>
/// A date/time in ExifTool's format, <c>YYYY:MM:DD[ HH:MM[:SS]][.fff][±HH:MM|Z]</c>, kept with its original shape
/// (whether it had a time, seconds, sub-seconds or a time zone) so shifting it doesn't change how it's written.
/// </summary>
public readonly partial record struct ExifDateTime(DateTime Value, bool HasTime, bool HasSeconds, string SubSeconds, string Zone)
{
    public static bool TryParse(string? text, out ExifDateTime result)
    {
        result = default;
        var match = text is null ? Match.Empty : Pattern().Match(text.Trim());
        if (!match.Success)
        {
            return false;
        }

        var hasTime = match.Groups["h"].Success;
        var hasSeconds = match.Groups["s"].Success;
        var stamp = string.Create(
            CultureInfo.InvariantCulture,
            $"{match.Groups["date"].Value} {(hasTime ? match.Groups["h"].Value : "00")}:{(hasTime ? match.Groups["m"].Value : "00")}:{(hasSeconds ? match.Groups["s"].Value : "00")}");
        if (!DateTime.TryParseExact(stamp, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
        {
            return false;
        }

        result = new ExifDateTime(value, hasTime, hasSeconds, match.Groups["sub"].Value, match.Groups["zone"].Value);
        return true;
    }

    public ExifDateTime Shift(TimeSpan offset) => this with { Value = Value + offset };

    /// <summary>Replaces the time zone label (e.g. "+01:00") without moving the clock time. Needs a time.</summary>
    public ExifDateTime WithZone(string zone) => HasTime ? this with { Zone = zone } : this;

    public override string ToString()
    {
        var text = Value.ToString("yyyy:MM:dd", CultureInfo.InvariantCulture);
        if (HasTime)
        {
            text += Value.ToString(HasSeconds ? " HH:mm:ss" : " HH:mm", CultureInfo.InvariantCulture) + SubSeconds + Zone;
        }

        return text;
    }

    /// <summary>True for "Z" or ±HH:MM within ±14:00.</summary>
    public static bool IsValidZone(string zone)
    {
        var match = ZonePattern().Match(zone ?? string.Empty);
        return match.Success && (zone == "Z" || (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 60) + int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) <= 14 * 60);
    }

    [GeneratedRegex(@"^(?<date>\d{4}:\d{2}:\d{2})(?: (?<h>\d{2}):(?<m>\d{2})(?::(?<s>\d{2}))?)?(?<sub>\.\d+)?(?<zone>Z|[+-]\d{2}:\d{2})?$")]
    private static partial Regex Pattern();

    [GeneratedRegex(@"^(?:Z|[+-](\d{2}):(\d{2}))$")]
    private static partial Regex ZonePattern();
}
