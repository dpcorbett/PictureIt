using System.Globalization;

namespace PictureIt.Core.Model;

/// <summary>Formats a <see cref="PhotoSummary"/> as short text for the status bar.</summary>
public static class PhotoSummaryFormatter
{
    private const string Separator = " · ";

    /// <summary>e.g. "Canon EOS R5 · RF50mm F1.8 STM · 1/250 s · f/2.8 · ISO 400 · 50 mm".</summary>
    public static string Describe(PhotoSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var parts = new List<string>();

        if (Camera(summary) is { } camera)
        {
            parts.Add(camera);
        }

        if (summary.LensModel is { } lens)
        {
            parts.Add(lens);
        }

        if (summary.ExposureTime is > 0 and var exposure)
        {
            parts.Add(Exposure(exposure));
        }

        if (summary.FNumber is > 0 and var aperture)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"f/{aperture:0.#}"));
        }

        if (summary.Iso is > 0 and var iso)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"ISO {iso}"));
        }

        if (summary.FocalLength is > 0 and var focal)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{focal:0.#} mm"));
        }

        if (summary.Width is { } width && summary.Height is { } height)
        {
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"{width} × {height}"));
        }

        return string.Join(Separator, parts);
    }

    /// <summary>The model, prefixed with the make unless the model already includes it ("Canon EOS R5", not "Canon Canon EOS R5").</summary>
    public static string? Camera(PhotoSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return (summary.CameraMake, summary.CameraModel) switch
        {
            ({ } make, { } model) when model.StartsWith(make.Split(' ')[0], StringComparison.OrdinalIgnoreCase) => model,
            ({ } make, { } model) => $"{make} {model}",
            (var make, var model) => model ?? make,
        };
    }

    public static string Exposure(double seconds) => seconds >= 1
        ? string.Create(CultureInfo.InvariantCulture, $"{seconds:0.#} s")
        : string.Create(CultureInfo.InvariantCulture, $"1/{Math.Round(1 / seconds):0} s");
}
