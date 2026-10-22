using PictureIt.Core.Model;
using PictureIt.Core.Organise;

namespace PictureIt.Core.Search;

/// <summary>What the catalogue knows about one photo: enough to search, filter and chart without opening the file.</summary>
public sealed record CatalogEntry
{
    public required string FilePath { get; init; }

    public long Length { get; init; }

    /// <summary>The file's last-write time (UTC) when it was indexed, to spot changes on the next scan.</summary>
    public DateTime Modified { get; init; }

    public DateTime? DateTaken { get; init; }

    public string? Camera { get; init; }

    public string? Lens { get; init; }

    public double? FocalLength { get; init; }

    public double? FNumber { get; init; }

    /// <summary>Seconds.</summary>
    public double? ExposureTime { get; init; }

    public int? Iso { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public int? Rating { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? Creator { get; init; }

    public string? Sublocation { get; init; }

    public string? City { get; init; }

    public string? State { get; init; }

    public string? Country { get; init; }

    public string? Copyright { get; init; }

    public IReadOnlyList<string> Keywords { get; init; } = [];

    public string FileName => Path.GetFileName(FilePath);

    public string Folder => Path.GetDirectoryName(FilePath) ?? string.Empty;

    public bool HasLocation => Latitude is not null && Longitude is not null;

    public static CatalogEntry FromMetadata(ImageMetadata metadata, long length, DateTime modifiedUtc)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var location = PhotoFacts.Location(metadata);
        return new CatalogEntry
        {
            FilePath = metadata.FilePath,
            Length = length,
            Modified = modifiedUtc,
            DateTaken = PhotoFacts.DateTaken(metadata),
            Camera = PhotoFacts.Camera(metadata),
            Lens = PhotoFacts.Lens(metadata),
            FocalLength = PhotoFacts.FocalLengthMm(metadata),
            FNumber = PhotoFacts.FNumber(metadata),
            ExposureTime = PhotoFacts.ExposureSeconds(metadata),
            Iso = PhotoFacts.IsoNumber(metadata),
            Width = PhotoFacts.WidthPixels(metadata),
            Height = PhotoFacts.HeightPixels(metadata),
            Latitude = location?.Latitude,
            Longitude = location?.Longitude,
            Rating = PhotoFacts.RatingNumber(metadata),
            Title = PhotoFacts.Title(metadata),
            Description = PhotoFacts.Description(metadata),
            Creator = PhotoFacts.Creator(metadata),
            Sublocation = PhotoFacts.Sublocation(metadata),
            City = PhotoFacts.City(metadata),
            State = PhotoFacts.State(metadata),
            Country = PhotoFacts.Country(metadata),
            Copyright = PhotoFacts.First(metadata, "MWG:Copyright", "XMP-dc:Rights", "IFD0:Copyright", "IPTC:CopyrightNotice"),
            Keywords = PhotoFacts.Keywords(metadata),
        };
    }
}
