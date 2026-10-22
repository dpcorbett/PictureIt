using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PictureIt.Core.Catalog;
using PictureIt.Core.Model;

namespace PictureIt.Core.Organise;

public enum ExportFields
{
    /// <summary>The editor's common fields, always all of them, so the file can be filled in and imported.</summary>
    Common,

    /// <summary>Every tag any of the photos has.</summary>
    All,
}

public enum ExportFormat
{
    Csv,
    Json,
}

/// <summary>
/// Writes photos' metadata as CSV or JSON in ExifTool's layout: a <c>SourceFile</c> column, then one column per tag
/// keyed <c>Group:Name</c>. <see cref="MetadataImport"/> reads the same layout back.
/// </summary>
public static class MetadataExport
{
    public const string SourceFileColumn = "SourceFile";

    private static readonly JsonWriterOptions JsonOptions = new()
    {
        Indented = true,

        // Keep accented and non-Latin text readable rather than \u-escaped.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static IReadOnlyList<TagKey> Columns(IEnumerable<ImageMetadata> photos, ExportFields fields)
    {
        ArgumentNullException.ThrowIfNull(photos);
        var common = TagCatalog.CommonFields.Select(f => f.Key).ToList();
        if (fields == ExportFields.Common)
        {
            return common;
        }

        var others = photos
            .SelectMany(p => p.Tags.Select(t => t.Key))
            .Distinct()
            .Where(k => !common.Contains(k))
            .OrderBy(TagCatalog.CategoryOf)
            .ThenBy(k => k.Group, StringComparer.OrdinalIgnoreCase)
            .ThenBy(k => k.Name, StringComparer.OrdinalIgnoreCase);
        return [.. common, .. others];
    }

    public static string Write(IReadOnlyList<ImageMetadata> photos, IReadOnlyList<TagKey> columns, ExportFormat format) =>
        format == ExportFormat.Json ? ToJson(photos, columns) : ToCsv(photos, columns);

    /// <summary>Lists are joined with <see cref="TagCatalog.ListSeparator"/>; missing tags are empty cells.</summary>
    public static string ToCsv(IReadOnlyList<ImageMetadata> photos, IReadOnlyList<TagKey> columns)
    {
        ArgumentNullException.ThrowIfNull(photos);
        ArgumentNullException.ThrowIfNull(columns);
        var header = new List<string> { SourceFileColumn };
        header.AddRange(columns.Select(c => c.ToString()));
        var rows = photos.Select(p => (IReadOnlyList<string>)[p.FilePath, .. columns.Select(c => p.TryGetTag(c, out var tag) ? TagCatalog.JoinList(tag.Values) : string.Empty)]);
        return Csv.Write([header, .. rows]);
    }

    /// <summary>One object per photo. Lists are arrays; tags a photo doesn't have are left out.</summary>
    public static string ToJson(IReadOnlyList<ImageMetadata> photos, IReadOnlyList<TagKey> columns)
    {
        ArgumentNullException.ThrowIfNull(photos);
        ArgumentNullException.ThrowIfNull(columns);
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, JsonOptions))
        {
            json.WriteStartArray();
            foreach (var photo in photos)
            {
                json.WriteStartObject();
                json.WriteString(SourceFileColumn, photo.FilePath);
                foreach (var column in columns)
                {
                    if (!photo.TryGetTag(column, out var tag))
                    {
                        continue;
                    }

                    if (tag.IsList)
                    {
                        json.WriteStartArray(column.ToString());
                        foreach (var value in tag.Values)
                        {
                            json.WriteStringValue(value);
                        }

                        json.WriteEndArray();
                    }
                    else
                    {
                        json.WriteString(column.ToString(), tag.DisplayValue);
                    }
                }

                json.WriteEndObject();
            }

            json.WriteEndArray();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
