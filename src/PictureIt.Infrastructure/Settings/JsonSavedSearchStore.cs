using PictureIt.Core.Search;

namespace PictureIt.Infrastructure.Settings;

/// <summary>Saved searches in a JSON file.</summary>
public sealed class JsonSavedSearchStore(string filePath) : ISavedSearchStore
{
    public static string DefaultFilePath => Path.Combine(JsonFile.DataDirectory, "searches.json");

    public IReadOnlyList<SavedSearch> Load() => JsonFile.Read<List<SavedSearch>>(filePath, []);

    public void Save(IReadOnlyList<SavedSearch> searches)
    {
        ArgumentNullException.ThrowIfNull(searches);
        JsonFile.Write(filePath, searches);
    }
}
