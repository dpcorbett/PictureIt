using System.Text.Json;
using PictureIt.Core.Catalog;
using PictureIt.Core.Model;

namespace PictureIt.Core.Organise;

/// <summary>One photo's values from an imported file. Blank cells aren't included, so they change nothing.</summary>
public sealed record ImportRow(string SourceFile, IReadOnlyDictionary<TagKey, IReadOnlyList<string>> Values);

/// <summary>An imported CSV or JSON file.</summary>
/// <param name="Columns">The tag columns, in file order.</param>
/// <param name="Rows">One per photo.</param>
public sealed record ImportTable(IReadOnlyList<TagKey> Columns, IReadOnlyList<ImportRow> Rows)
{
    /// <summary>Columns that can't be written (file information and values ExifTool works out).</summary>
    public IReadOnlyList<TagKey> ReadOnlyColumns => [.. Columns.Where(c => !MetadataImport.IsImportable(c))];
}

/// <summary>Rows matched to photos, and the rows that matched nothing.</summary>
public sealed record ImportMatch(IReadOnlyList<(string FilePath, ImportRow Row)> Matched, IReadOnlyList<string> Unmatched);

/// <summary>The edits for one photo, and values that were skipped because they aren't valid.</summary>
public sealed record ImportEdits(IReadOnlyList<MetadataEdit> Edits, IReadOnlyList<string> Problems);

/// <summary>
/// Reads metadata back from the CSV or JSON that <see cref="MetadataExport"/> writes (or that ExifTool's <c>-csv</c>
/// and <c>-json</c> write), and turns it into edits that go through the usual preview. Only values that differ from
/// the photo's current ones become edits, so exporting, changing a few cells and importing changes just those cells.
/// </summary>
public static class MetadataImport
{
    private static readonly HashSet<string> ReadOnlyGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        "Composite", "File", "System", "ExifTool",
    };

    public static bool IsImportable(TagKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return !key.IsPattern && !ReadOnlyGroups.Contains(key.Group);
    }

    /// <summary>Reads a file by its extension (.json, otherwise CSV). Throws <see cref="InvalidDataException"/> with a readable message.</summary>
    public static ImportTable Parse(string text, string extension) =>
        string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase) ? ParseJson(text) : ParseCsv(text);

    public static ImportTable ParseCsv(string text)
    {
        var rows = Csv.Read(text);
        if (rows.Count == 0)
        {
            throw new InvalidDataException("The file is empty.");
        }

        var header = rows[0];
        var sourceColumn = header.ToList().FindIndex(h => h.Trim().Equals(MetadataExport.SourceFileColumn, StringComparison.OrdinalIgnoreCase));
        if (sourceColumn < 0)
        {
            throw new InvalidDataException($"The first row needs a \"{MetadataExport.SourceFileColumn}\" column naming each photo.");
        }

        var columns = new Dictionary<int, TagKey>();
        for (var i = 0; i < header.Count; i++)
        {
            if (i == sourceColumn || header[i].Trim().Length == 0)
            {
                continue;
            }

            columns[i] = ParseColumn(header[i]);
        }

        var importRows = rows.Skip(1).Select(row =>
        {
            var values = new Dictionary<TagKey, IReadOnlyList<string>>();
            foreach (var (index, key) in columns)
            {
                var cell = index < row.Count ? row[index].Trim() : string.Empty;
                if (cell.Length > 0)
                {
                    values[key] = TagCatalog.IsListTag(key) ? TagCatalog.SplitList(cell) : [cell];
                }
            }

            return new ImportRow(sourceColumn < row.Count ? row[sourceColumn].Trim() : string.Empty, values);
        });

        return new ImportTable([.. columns.Values], [.. importRows.Where(r => r.SourceFile.Length > 0)]);
    }

    public static ImportTable ParseJson(string text)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The file isn't valid JSON: {ex.Message}", ex);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("The file should hold a list of photos, like ExifTool's -json output.");
            }

            var columns = new List<TagKey>();
            var rows = new List<ImportRow>();
            foreach (var photo in document.RootElement.EnumerateArray())
            {
                if (photo.ValueKind != JsonValueKind.Object
                    || !photo.TryGetProperty(MetadataExport.SourceFileColumn, out var source)
                    || source.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException($"Every photo needs a \"{MetadataExport.SourceFileColumn}\".");
                }

                var values = new Dictionary<TagKey, IReadOnlyList<string>>();
                foreach (var property in photo.EnumerateObject().Where(p => p.Name != MetadataExport.SourceFileColumn))
                {
                    var key = ParseColumn(property.Name);
                    if (!columns.Contains(key))
                    {
                        columns.Add(key);
                    }

                    var items = property.Value.ValueKind == JsonValueKind.Array
                        ? property.Value.EnumerateArray().Select(JsonText).ToList()
                        : [JsonText(property.Value)];
                    items = [.. items.Where(v => v.Length > 0)];
                    if (items.Count > 0)
                    {
                        values[key] = items;
                    }
                }

                rows.Add(new ImportRow(source.GetString()!, values));
            }

            return new ImportTable(columns, rows);
        }
    }

    /// <summary>
    /// Matches rows to photos by full path, or else by file name (so a file exported from another copy of the folder
    /// still works).
    /// </summary>
    public static ImportMatch Match(ImportTable table, IReadOnlyCollection<string> photoPaths)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(photoPaths);
        var byPath = photoPaths.ToDictionary(p => p, StringComparer.OrdinalIgnoreCase);
        var byName = photoPaths.GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key!, g => g.Single(), StringComparer.OrdinalIgnoreCase);

        var matched = new List<(string, ImportRow)>();
        var unmatched = new List<string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in table.Rows)
        {
            if ((byPath.TryGetValue(row.SourceFile, out var path) || byName.TryGetValue(Path.GetFileName(row.SourceFile), out path))
                && used.Add(path))
            {
                matched.Add((path, row));
            }
            else
            {
                unmatched.Add(row.SourceFile);
            }
        }

        return new ImportMatch(matched, unmatched);
    }

    public static ImportEdits EditsFor(ImportRow row, ImageMetadata current)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(current);
        var edits = new List<MetadataEdit>();
        var problems = new List<string>();

        foreach (var (key, values) in row.Values)
        {
            var exists = current.TryGetTag(key, out var tag);
            if (!IsImportable(key) || (exists && !tag.IsWritable))
            {
                continue;
            }

            var isList = TagCatalog.IsListTag(key) || (exists && tag.IsList);
            var newValues = isList ? values : [TagCatalog.JoinList(values)];
            if (exists && tag.Values.SequenceEqual(newValues, StringComparer.Ordinal))
            {
                continue;
            }

            var editor = TagCatalog.EditorFor(key, isList);
            if (newValues.Select(v => TagCatalog.Validate(editor, v)).FirstOrDefault(e => e is not null) is { } error)
            {
                problems.Add($"{Path.GetFileName(current.FilePath)}, {key}: \"{TagCatalog.JoinList(newValues)}\". {error}");
                continue;
            }

            edits.Add(isList ? MetadataEdit.Set(key, newValues) : MetadataEdit.Set(key, newValues[0]));
        }

        return new ImportEdits(edits, problems);
    }

    private static TagKey ParseColumn(string header)
    {
        var text = header.Trim();
        return TagKey.TryParse(text, out var key) && !key.IsPattern
            ? key
            : throw new InvalidDataException($"\"{text}\" isn't a tag. Column headings should look like Group:Name, e.g. XMP-dc:Title.");
    }

    private static string JsonText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!.Trim(),
        JsonValueKind.Null => string.Empty,
        _ => value.GetRawText(),
    };
}
