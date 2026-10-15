using PictureIt.Core.Catalog;
using PictureIt.Core.Model;

namespace PictureIt.Core.Tests;

internal static class TestMetadata
{
    /// <summary>Builds metadata from (key, value, raw) entries; values containing "; " become lists.</summary>
    public static ImageMetadata Create(params (string Key, string Value, string? Raw)[] tags) => Create(@"C:\photos\a.jpg", tags);

    public static ImageMetadata Create(string path, params (string Key, string Value, string? Raw)[] tags) =>
        new(path, tags.Select(t =>
        {
            var key = TagKey.Parse(t.Key);
            var values = t.Value.Split("; ");
            var readOnly = key.Group is "File" or "Composite" or "System" or "ExifTool";
            return new MetadataTag(key, key.Name, values, t.Raw, IsList: values.Length > 1 || TagCatalog.IsListTag(key), IsWritable: !readOnly);
        }));

    public static ImageMetadata Create(params (string Key, string Value)[] tags) =>
        Create([.. tags.Select(t => (t.Key, t.Value, (string?)null))]);
}
