using PictureIt.Core.Model;

namespace PictureIt.Core.Abstractions;

/// <summary>
/// Fast, read-only access to the key fields of a photo, for browsing large folders.
/// Not a source of editable tags: use <see cref="IMetadataReader"/> for those.
/// </summary>
public interface IPhotoSummaryReader
{
    /// <summary>Returns null if the file isn't a readable image.</summary>
    PhotoSummary? ReadSummary(string filePath);
}
