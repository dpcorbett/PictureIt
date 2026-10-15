using System.Text.Json;
using PictureIt.Core.Abstractions;
using PictureIt.Core.Model;

namespace PictureIt.Infrastructure.ExifTool;

/// <summary>
/// Reads every tag ExifTool can find, using group-1 names (<c>IFD0:Model</c>, <c>XMP-dc:Subject</c>) so the
/// keys match exactly what <see cref="ExifToolWriter"/> writes.
/// </summary>
public sealed class ExifToolReader(ExifToolProcess exifTool) : IMetadataReader
{
    // Files per ExifTool command; keeps each JSON response a manageable size.
    private const int BatchSize = 100;

    // Groups whose tags are computed or come from the file system, not from metadata in the file.
    private static readonly HashSet<string> ReadOnlyGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        "ExifTool", "System", "File", "Composite",
    };

    public async Task<ImageMetadata> ReadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var results = await ReadManyAsync([filePath], cancellationToken).ConfigureAwait(false);
        var result = results[0];
        return result.Metadata ?? throw new ExifToolException($"Could not read '{filePath}': {result.Error}");
    }

    public async Task<IReadOnlyList<MetadataReadResult>> ReadManyAsync(
        IEnumerable<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        var paths = filePaths.Select(Path.GetFullPath).ToArray();
        var results = new List<MetadataReadResult>(paths.Length);

        foreach (var batch in paths.Chunk(BatchSize))
        {
            // -j: JSON; -G1: specific group names; -l: description, formatted value and raw value per tag.
            string[] args = ["-j", "-G1", "-l", .. batch];
            var response = await exifTool.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            var parsed = Parse(response.Output);

            foreach (var path in batch)
            {
                if (!parsed.TryGetValue(path, out var metadata))
                {
                    results.Add(new MetadataReadResult(path, null, ErrorFor(path, response.Errors) ?? "File could not be read."));
                }
                else if (metadata.GetValue("ExifTool:Error") is { } error)
                {
                    results.Add(new MetadataReadResult(path, null, error));
                }
                else
                {
                    results.Add(new MetadataReadResult(path, metadata, null));
                }
            }
        }

        return results;
    }

    internal static Dictionary<string, ImageMetadata> Parse(string json)
    {
        var files = new Dictionary<string, ImageMetadata>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
        {
            return files;
        }

        using var document = JsonDocument.Parse(json);
        foreach (var file in document.RootElement.EnumerateArray())
        {
            string? sourceFile = null;
            var tags = new List<MetadataTag>();

            foreach (var property in file.EnumerateObject())
            {
                if (property.NameEquals("SourceFile"))
                {
                    sourceFile = property.Value.GetString();
                }
                else if (TagKey.TryParse(property.Name, out var key) && property.Value.ValueKind == JsonValueKind.Object)
                {
                    tags.Add(ParseTag(key, property.Value));
                }
            }

            if (sourceFile is not null)
            {
                // ExifTool echoes Windows paths with forward slashes.
                var path = Path.GetFullPath(sourceFile);
                files[path] = new ImageMetadata(path, tags);
            }
        }

        return files;
    }

    private static MetadataTag ParseTag(TagKey key, JsonElement element)
    {
        var description = element.TryGetProperty("desc", out var desc) ? desc.GetString() ?? key.Name : key.Name;

        IReadOnlyList<string> values = [];
        var isList = false;
        if (element.TryGetProperty("val", out var val))
        {
            isList = val.ValueKind == JsonValueKind.Array;
            values = isList ? [.. val.EnumerateArray().Select(ToText)] : [ToText(val)];
        }

        string? raw = null;
        if (element.TryGetProperty("num", out var num))
        {
            raw = num.ValueKind == JsonValueKind.Array
                ? string.Join(", ", num.EnumerateArray().Select(ToText))
                : ToText(num);
        }

        return new MetadataTag(key, description, values, raw, isList, IsWritable: !ReadOnlyGroups.Contains(key.Group));
    }

    private static string ToText(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => bool.TrueString,
        JsonValueKind.False => bool.FalseString,
        JsonValueKind.Null => string.Empty,
        _ => element.GetRawText(),
    };

    // ExifTool reports a missing or unreadable file as "Error: <reason> - <path>" on stderr.
    private static string? ErrorFor(string path, string errors) =>
        errors.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith("Error", StringComparison.Ordinal))
            .FirstOrDefault(line => line.Contains(Path.GetFileName(path), StringComparison.OrdinalIgnoreCase));
}
