using System.Buffers;
using System.Text;

namespace PictureIt.Core.Organise;

/// <summary>
/// Minimal RFC 4180 CSV: fields with commas, quotes or line breaks are quoted, and quotes are doubled.
/// Reading also accepts semicolon- and tab-separated files, which Excel saves in some regions.
/// </summary>
public static class Csv
{
    private static readonly char[] Separators = [',', ';', '\t'];

    private static readonly SearchValues<char> NeedsQuotes = SearchValues.Create(",\"\r\n;\t");

    public static string Write(IEnumerable<IReadOnlyList<string>> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var builder = new StringBuilder();
        foreach (var row in rows)
        {
            builder.AppendJoin(',', row.Select(Quote)).Append("\r\n");
        }

        return builder.ToString();
    }

    public static IReadOnlyList<IReadOnlyList<string>> Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var separator = DetectSeparator(text);
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c != '"')
                {
                    field.Append(c);
                }
                else if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else
                {
                    quoted = false;
                }
            }
            else if (c == '"' && field.Length == 0)
            {
                quoted = true;
            }
            else if (c == separator)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(field.ToString());
                field.Clear();
                AddRow(rows, row);
                row = [];
            }
            else
            {
                field.Append(c);
            }
        }

        if (quoted)
        {
            throw new InvalidDataException("The file ends inside a quoted value.");
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            AddRow(rows, row);
        }

        return rows;
    }

    private static void AddRow(List<IReadOnlyList<string>> rows, List<string> row)
    {
        // Blank lines (including the one after the last row) aren't rows.
        if (row.Count > 1 || row[0].Length > 0)
        {
            rows.Add(row);
        }
    }

    private static char DetectSeparator(string text)
    {
        var end = text.IndexOfAny(['\r', '\n']);
        var header = end < 0 ? text : text[..end];
        return Separators.MaxBy(c => header.Count(h => h == c));
    }

    private static string Quote(string field)
    {
        var needsQuotes = field.AsSpan().IndexOfAny(NeedsQuotes) >= 0 || (field.Length > 0 && (char.IsWhiteSpace(field[0]) || char.IsWhiteSpace(field[^1])));
        return needsQuotes ? $"\"{field.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : field;
    }
}
