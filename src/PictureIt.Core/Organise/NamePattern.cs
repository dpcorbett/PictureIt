using System.Globalization;
using System.Text;
using PictureIt.Core.Model;

namespace PictureIt.Core.Organise;

/// <summary>A token that can appear in a <see cref="NamePattern"/>, for menus and help.</summary>
/// <param name="Name">What goes between the braces, e.g. "DateTaken".</param>
/// <param name="DisplayName">Friendly name, e.g. "Date taken".</param>
/// <param name="Example">What to insert, e.g. "{DateTaken:yyyy-MM-dd}".</param>
public sealed record NameToken(string Name, string DisplayName, string Example);

/// <summary>
/// A file or folder name built from a photo's metadata, e.g. <c>{DateTaken:yyyy-MM-dd}_{Seq:000}</c>.
/// <list type="bullet">
/// <item><c>{Token}</c>: one of <see cref="Tokens"/>, or any tag by its key (<c>{IFD0:Model}</c>).</item>
/// <item><c>{DateTaken:format}</c>: a .NET date format; <c>{Seq:000}</c>: the number padded to that many digits.</item>
/// <item><c>{Token|text}</c>: <c>text</c> when the photo doesn't have the value (otherwise it's left out).</item>
/// <item><c>{{</c> and <c>}}</c>: literal braces. In folder patterns, <c>\</c> or <c>/</c> separates folders.</item>
/// </list>
/// </summary>
public sealed class NamePattern
{
    public static IReadOnlyList<NameToken> Tokens { get; } =
    [
        new("Name", "Original name", "{Name}"),
        new("DateTaken", "Date taken", "{DateTaken:yyyy-MM-dd}"),
        new("Seq", "Sequence number", "{Seq:000}"),
        new("Camera", "Camera", "{Camera}"),
        new("Make", "Camera make", "{Make}"),
        new("Model", "Camera model", "{Model}"),
        new("Lens", "Lens", "{Lens}"),
        new("FocalLength", "Focal length", "{FocalLength}"),
        new("Aperture", "Aperture", "{Aperture}"),
        new("ISO", "ISO", "{ISO}"),
        new("Width", "Width", "{Width}"),
        new("Height", "Height", "{Height}"),
        new("Title", "Title", "{Title}"),
        new("Creator", "Creator", "{Creator}"),
        new("Rating", "Rating", "{Rating}"),
        new("Location", "Sublocation", "{Location}"),
        new("City", "City", "{City}"),
        new("State", "State / Province", "{State}"),
        new("Country", "Country", "{Country}"),
    ];

    private static readonly Dictionary<string, NameToken> TokensByName = Tokens.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, Func<ImageMetadata, string?>> Facts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Camera"] = PhotoFacts.Camera,
        ["Make"] = PhotoFacts.Make,
        ["Model"] = PhotoFacts.Model,
        ["Lens"] = PhotoFacts.Lens,
        ["FocalLength"] = PhotoFacts.FocalLength,
        ["Aperture"] = PhotoFacts.Aperture,
        ["ISO"] = PhotoFacts.Iso,
        ["Width"] = PhotoFacts.Width,
        ["Height"] = PhotoFacts.Height,
        ["Title"] = PhotoFacts.Title,
        ["Creator"] = PhotoFacts.Creator,
        ["Rating"] = PhotoFacts.Rating,
        ["Location"] = PhotoFacts.Sublocation,
        ["City"] = PhotoFacts.City,
        ["State"] = PhotoFacts.State,
        ["Country"] = PhotoFacts.Country,
    };

    // Windows also rejects these, whatever the extension.
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static readonly char[] InvalidNameChars = [.. Path.GetInvalidFileNameChars().Union(['\\', '/', ':', '*', '?', '"', '<', '>', '|'])];

    private readonly IReadOnlyList<Part> _parts;

    private NamePattern(string text, IReadOnlyList<Part> parts)
    {
        Text = text;
        _parts = parts;
    }

    public string Text { get; }

    /// <summary>True if the pattern contains a folder separator.</summary>
    public bool HasFolders => _parts.Any(p => p is Literal { Text: var t } && t.Contains('\\', StringComparison.Ordinal));

    /// <summary>True if the pattern uses <c>{Seq}</c>.</summary>
    public bool UsesSequence => _parts.Any(p => p is Token { Name: "Seq" });

    /// <summary>True if the pattern uses <c>{Name}</c>, which keeps names unique.</summary>
    public bool UsesOriginalName => _parts.Any(p => p is Token { Name: "Name" });

    public static NamePattern Parse(string text) =>
        TryParse(text, out var pattern, out var error) ? pattern : throw new FormatException(error);

    public static bool TryParse(string? text, out NamePattern pattern, out string? error)
    {
        pattern = null!;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Enter a pattern.";
            return false;
        }

        var parts = new List<Part>();
        var literal = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '{' or '}' && i + 1 < text.Length && text[i + 1] == c)
            {
                literal.Append(c);
                i++;
                continue;
            }

            if (c == '}')
            {
                error = "There's a } without a matching {. Use }} for a brace in the name.";
                return false;
            }

            if (c != '{')
            {
                literal.Append(c == '/' ? '\\' : c);
                continue;
            }

            var end = text.IndexOf('}', i + 1);
            if (end < 0)
            {
                error = "There's a { without a matching }. Use {{ for a brace in the name.";
                return false;
            }

            if (!TryParseToken(text[(i + 1)..end], out var token, out error))
            {
                return false;
            }

            if (literal.Length > 0)
            {
                parts.Add(new Literal(literal.ToString()));
                literal.Clear();
            }

            parts.Add(token);
            i = end;
        }

        if (literal.Length > 0)
        {
            parts.Add(new Literal(literal.ToString()));
        }

        pattern = new NamePattern(text, parts);
        return true;
    }

    /// <summary>
    /// Builds the name for one photo. Folder separators are kept as <c>\</c>, and each folder or file name is made
    /// safe for Windows (invalid characters become <c>_</c>, trailing dots and spaces are removed, empty folders are
    /// dropped).
    /// </summary>
    /// <param name="metadata">The photo.</param>
    /// <param name="sequence">The value of <c>{Seq}</c>.</param>
    /// <param name="culture">For month and day names in dates; defaults to the current culture.</param>
    public NameResult Render(ImageMetadata metadata, int sequence, IFormatProvider? culture = null)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        culture ??= CultureInfo.CurrentCulture;
        var text = new StringBuilder();
        var missing = new List<string>();

        foreach (var part in _parts)
        {
            switch (part)
            {
                case Literal literal:
                    text.Append(literal.Text);
                    break;
                case Token token:
                    var value = Value(token, metadata, sequence, culture);
                    if (value is null)
                    {
                        missing.Add(token.DisplayName);
                        value = token.Fallback ?? string.Empty;
                    }

                    // A value can't add folders, only the pattern can.
                    text.Append(Clean(value));
                    break;
            }
        }

        var segments = text.ToString().Split('\\').Select(CleanSegment).Where(s => s.Length > 0);
        return new NameResult(string.Join('\\', segments), [.. missing.Distinct()]);
    }

    private static bool TryParseToken(string body, out Token token, out string? error)
    {
        token = null!;
        error = null;
        string? fallback = null;
        var bar = body.IndexOf('|', StringComparison.Ordinal);
        if (bar >= 0)
        {
            fallback = body[(bar + 1)..];
            body = body[..bar];
        }

        var colon = body.IndexOf(':', StringComparison.Ordinal);
        var name = (colon < 0 ? body : body[..colon]).Trim();
        var format = colon < 0 ? null : body[(colon + 1)..];

        if (TokensByName.TryGetValue(name, out var known))
        {
            if (format is not null && known.Name is not ("DateTaken" or "Seq"))
            {
                error = $"{{{known.Name}}} doesn't take a format.";
                return false;
            }

            if (known.Name == "DateTaken" && format is not null && !IsValidDateFormat(format))
            {
                error = $"\"{format}\" isn't a date format. Try yyyy-MM-dd.";
                return false;
            }

            if (known.Name == "Seq" && format is not null && (format.Length == 0 || format.Any(c => c != '0')))
            {
                error = "Write the sequence as {Seq} or with zeros for the digits, e.g. {Seq:000}.";
                return false;
            }

            token = new Token(known.Name, known.DisplayName, null, format, fallback);
            return true;
        }

        // Anything else must be a tag key, e.g. {IFD0:Model}.
        if (colon > 0 && TagKey.TryParse(body.Trim(), out var key) && !key.IsPattern)
        {
            token = new Token(key.ToString(), key.ToString(), key, null, fallback);
            return true;
        }

        error = body.Trim().Length == 0 ? "There's an empty {}." : $"{{{body.Trim()}}} isn't a token or a tag.";
        return false;
    }

    private static bool IsValidDateFormat(string format)
    {
        if (format.Length == 0)
        {
            return false;
        }

        try
        {
            _ = new DateTime(2000, 1, 2, 3, 4, 5, DateTimeKind.Unspecified).ToString(format, CultureInfo.InvariantCulture);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string? Value(Token token, ImageMetadata metadata, int sequence, IFormatProvider culture)
    {
        if (token.Key is { } key)
        {
            return metadata.GetValue(key)?.Trim() is { Length: > 0 } tagValue ? tagValue : null;
        }

        return token.Name switch
        {
            "Name" => Path.GetFileNameWithoutExtension(metadata.FilePath),
            "Seq" => sequence.ToString(token.Format ?? "0", CultureInfo.InvariantCulture),
            "DateTaken" => PhotoFacts.DateTaken(metadata)?.ToString(token.Format ?? "yyyy-MM-dd", culture),
            _ => Facts[token.Name](metadata),
        };
    }

    private static string Clean(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(InvalidNameChars.Contains(c) || char.IsControl(c) ? '_' : c);
        }

        return builder.ToString();
    }

    private static string CleanSegment(string segment)
    {
        var cleaned = Clean(segment).Trim().TrimEnd('.', ' ');
        return ReservedNames.Contains(cleaned.Split('.')[0]) ? "_" + cleaned : cleaned;
    }

    private abstract record Part;

    private sealed record Literal(string Text) : Part;

    private sealed record Token(string Name, string DisplayName, TagKey? Key, string? Format, string? Fallback) : Part;
}

/// <summary>A rendered name, and the values the photo didn't have.</summary>
/// <param name="Name">The name (with <c>\</c> between folders), possibly empty.</param>
/// <param name="Missing">Friendly names of the tokens that had no value.</param>
public sealed record NameResult(string Name, IReadOnlyList<string> Missing);
