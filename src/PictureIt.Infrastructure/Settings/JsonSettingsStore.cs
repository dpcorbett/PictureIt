using System.Text.Json;
using System.Text.Json.Serialization;
using PictureIt.Core.Settings;

namespace PictureIt.Infrastructure.Settings;

/// <summary>Keeps <see cref="AppSettings"/> in a JSON file. A missing or unreadable file means defaults.</summary>
public sealed class JsonSettingsStore(string filePath) : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PictureIt", "settings.json");

    public AppSettings Load()
    {
        try
        {
            return File.Exists(filePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(filePath), JsonOptions) ?? new AppSettings()
                : new AppSettings();
        }
        catch (JsonException)
        {
            // A corrupt settings file shouldn't stop the app starting.
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        // Write then swap, so a crash mid-write can't leave a truncated file.
        var temp = filePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, filePath, overwrite: true);
    }
}
