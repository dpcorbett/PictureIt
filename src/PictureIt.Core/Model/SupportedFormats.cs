namespace PictureIt.Core.Model;

/// <summary>The image files the library shows.</summary>
public static class SupportedFormats
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".png", ".tif", ".tiff", ".heic", ".heif", ".avif", ".webp", ".gif", ".bmp", ".jxl",
        // Camera raw
        ".dng", ".cr2", ".cr3", ".crw", ".nef", ".nrw", ".arw", ".srf", ".sr2", ".orf", ".rw2", ".raf", ".pef", ".srw", ".x3f", ".3fr", ".iiq",
    };

    public static bool IsSupported(string path) => Extensions.Contains(Path.GetExtension(path));
}
