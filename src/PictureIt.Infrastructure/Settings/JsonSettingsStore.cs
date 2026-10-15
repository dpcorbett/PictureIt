using PictureIt.Core.Settings;

namespace PictureIt.Infrastructure.Settings;

/// <summary>Keeps <see cref="AppSettings"/> in a JSON file. A missing or unreadable file means defaults.</summary>
public sealed class JsonSettingsStore(string filePath) : ISettingsStore
{
    public static string DefaultFilePath => Path.Combine(JsonFile.DataDirectory, "settings.json");

    public AppSettings Load() => JsonFile.Read(filePath, new AppSettings());

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        JsonFile.Write(filePath, settings);
    }
}
