using System.Text.Json;
using System.Text.Json.Serialization;

namespace PictureIt.Infrastructure.Settings;

/// <summary>Shared JSON file handling for the app's small data files (settings, history, templates).</summary>
internal static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PictureIt");

    /// <summary>Reads a file, returning <paramref name="fallback"/> if it's missing or unreadable.</summary>
    public static T Read<T>(string path, T fallback)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? fallback : fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
        catch (IOException)
        {
            return fallback;
        }
    }

    /// <summary>Writes via a temp file and swap, so a crash mid-write can't leave a truncated file.</summary>
    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, overwrite: true);
    }
}
