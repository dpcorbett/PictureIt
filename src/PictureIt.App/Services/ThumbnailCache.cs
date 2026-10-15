using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace PictureIt.App.Services;

/// <summary>
/// Loads images through the Windows shell thumbnail cache, which is fast, handles any format Windows can show
/// (including camera raw with the Raw Image Extension) and never loads a full-size image into memory.
/// </summary>
public sealed class ThumbnailCache
{
    private const int MaxEntries = 600;

    // The largest size GetThumbnailAsync accepts.
    private const uint MaxThumbnailSize = 1600;

    private readonly Dictionary<string, BitmapImage> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _order = new();

    /// <summary>A small cached thumbnail for the library grid.</summary>
    public async Task<BitmapImage?> GetThumbnailAsync(string path)
    {
        if (_cache.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var image = await LoadAsync(path, 256);
        if (image is not null)
        {
            _cache[path] = image;
            _order.Enqueue(path);
            while (_order.Count > MaxEntries)
            {
                _cache.Remove(_order.Dequeue());
            }
        }

        return image;
    }

    /// <summary>
    /// A large, uncached image for the preview pane: the file itself decoded at up to 2560px, or the shell's
    /// largest thumbnail (1600px) when Windows has no decoder for the format but can still show a thumbnail.
    /// </summary>
    public static async Task<BitmapImage?> GetPreviewAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var image = new BitmapImage { DecodePixelWidth = 2560, DecodePixelType = DecodePixelType.Physical };
            await image.SetSourceAsync(stream);
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return await LoadAsync(path, MaxThumbnailSize);
        }
    }

    public void Clear()
    {
        _cache.Clear();
        _order.Clear();
    }

    private static async Task<BitmapImage?> LoadAsync(string path, uint size)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, size, ThumbnailOptions.ResizeThumbnail);
            if (thumbnail is null)
            {
                return null;
            }

            var image = new BitmapImage();
            await image.SetSourceAsync(thumbnail);
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            // Missing, locked or undecodable file: show the placeholder.
            return null;
        }
    }
}
