using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;
using PictureIt.Core.Abstractions;
using PictureIt.Core.Model;
using MetadataDirectory = MetadataExtractor.Directory;

namespace PictureIt.Infrastructure.Summary;

/// <summary>
/// Reads <see cref="PhotoSummary"/> fields in-process with MetadataExtractor, which is much faster than a
/// round trip to ExifTool when filling a grid of thumbnails.
/// </summary>
public sealed class MetadataExtractorSummaryReader : IPhotoSummaryReader
{
    public PhotoSummary? ReadSummary(string filePath)
    {
        IReadOnlyList<MetadataDirectory> directories;
        try
        {
            directories = ImageMetadataReader.ReadMetadata(filePath);
        }
        catch (ImageProcessingException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }

        var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        var exif = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
        var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
        var location = gps is not null && gps.TryGetGeoLocation(out var geo) ? geo : (GeoLocation?)null;
        var (width, height) = Dimensions(directories);

        return new PhotoSummary
        {
            CameraMake = Text(ifd0, ExifDirectoryBase.TagMake),
            CameraModel = Text(ifd0, ExifDirectoryBase.TagModel),
            LensModel = Text(exif, ExifDirectoryBase.TagLensModel),
            DateTaken = exif is not null && exif.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var taken) ? taken : null,
            Width = width,
            Height = height,
            ExposureTime = Rational(exif, ExifDirectoryBase.TagExposureTime),
            FNumber = Rational(exif, ExifDirectoryBase.TagFNumber),
            Iso = exif is not null && exif.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out var iso) ? iso : null,
            FocalLength = Rational(exif, ExifDirectoryBase.TagFocalLength),
            Latitude = location?.Latitude,
            Longitude = location?.Longitude,
        };
    }

    private static (int? Width, int? Height) Dimensions(IReadOnlyList<MetadataDirectory> directories)
    {
        if (directories.OfType<JpegDirectory>().FirstOrDefault() is { } jpeg
            && jpeg.TryGetInt32(JpegDirectory.TagImageWidth, out var jw)
            && jpeg.TryGetInt32(JpegDirectory.TagImageHeight, out var jh))
        {
            return (jw, jh);
        }

        if (directories.OfType<PngDirectory>().FirstOrDefault(d => d.Name == "PNG-IHDR") is { } png
            && png.TryGetInt32(PngDirectory.TagImageWidth, out var pw)
            && png.TryGetInt32(PngDirectory.TagImageHeight, out var ph))
        {
            return (pw, ph);
        }

        if (directories.OfType<ExifSubIfdDirectory>().FirstOrDefault() is { } exif
            && exif.TryGetInt32(ExifDirectoryBase.TagExifImageWidth, out var ew)
            && exif.TryGetInt32(ExifDirectoryBase.TagExifImageHeight, out var eh))
        {
            return (ew, eh);
        }

        return (null, null);
    }

    private static string? Text(MetadataDirectory? directory, int tag)
    {
        var value = directory?.GetString(tag)?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static double? Rational(MetadataDirectory? directory, int tag) =>
        directory is not null && directory.TryGetRational(tag, out var value) && value.Denominator != 0
            ? value.ToDouble()
            : null;
}
