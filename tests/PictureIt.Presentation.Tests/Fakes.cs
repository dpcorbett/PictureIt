using PictureIt.Core.Model;
using PictureIt.Core.Settings;

namespace PictureIt.Presentation.Tests;

internal sealed class InMemorySettingsStore : ISettingsStore
{
    public AppSettings Current { get; set; } = new();

    public int SaveCount { get; private set; }

    public AppSettings Load() => Current;

    public void Save(AppSettings settings)
    {
        Current = settings;
        SaveCount++;
    }
}

internal static class Metadata
{
    public const string Path = @"C:\photos\a.jpg";

    /// <summary>Builds metadata from "Group:Name" → value pairs; values containing "; " become lists.</summary>
    public static ImageMetadata Create(string path, params (string Key, string Value)[] tags) =>
        new(path, tags.Select(t =>
        {
            var key = TagKey.Parse(t.Key);
            var values = t.Value.Split("; ");
            var readOnly = key.Group is "File" or "Composite" or "System";
            return new MetadataTag(key, key.Name, values, null, IsList: values.Length > 1, IsWritable: !readOnly);
        }));

    public static ImageMetadata Sample(string path = Path) => Create(
        path,
        ("IFD0:Model", "Canon EOS R5"),
        ("MWG:Keywords", "London; Big Ben"),
        ("MWG:Description", "Old description"),
        ("ExifIFD:ExposureTime", "1/250"),
        ("File:FileType", "JPEG"),
        ("Composite:GPSPosition", "51 deg N, 0 deg W"));
}
