namespace PictureIt.Core.Model;

/// <summary>
/// Result of reading one file as part of a batch read. One unreadable file
/// shouldn't stop a folder of others from loading, so failures are reported per file.
/// </summary>
public sealed record MetadataReadResult(string FilePath, ImageMetadata? Metadata, string? Error)
{
    public bool Succeeded => Metadata is not null;
}
