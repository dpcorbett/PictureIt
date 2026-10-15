namespace PictureIt.Core.Model;

/// <summary>
/// The handful of fields shown in thumbnails and the status bar, read quickly without ExifTool.
/// All fields are optional because many files lack some or all of them.
/// </summary>
public sealed record PhotoSummary
{
    public string? CameraMake { get; init; }

    public string? CameraModel { get; init; }

    public string? LensModel { get; init; }

    public DateTime? DateTaken { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    /// <summary>Exposure time in seconds.</summary>
    public double? ExposureTime { get; init; }

    public double? FNumber { get; init; }

    public int? Iso { get; init; }

    /// <summary>Focal length in millimetres.</summary>
    public double? FocalLength { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public bool HasLocation => Latitude is not null && Longitude is not null;
}
