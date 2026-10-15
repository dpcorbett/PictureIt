using System.Text.Json;
using PictureIt.Core.Templates;

namespace PictureIt.Infrastructure.Settings;

/// <summary>Templates in a JSON file, plus single-template files for sharing.</summary>
public sealed class JsonTemplateStore(string filePath) : ITemplateStore
{
    public static string DefaultFilePath => Path.Combine(JsonFile.DataDirectory, "templates.json");

    public IReadOnlyList<MetadataTemplate> Load() => JsonFile.Read<List<MetadataTemplate>>(filePath, []);

    public void Save(IReadOnlyList<MetadataTemplate> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        JsonFile.Write(filePath, templates);
    }

    public void Export(MetadataTemplate metadataTemplate, string path)
    {
        ArgumentNullException.ThrowIfNull(metadataTemplate);
        File.WriteAllText(path, JsonSerializer.Serialize(metadataTemplate, JsonFile.Options));
    }

    /// <summary>Reads a template file. Throws <see cref="InvalidDataException"/> if it isn't one.</summary>
    public MetadataTemplate Import(string path)
    {
        try
        {
            var template = JsonSerializer.Deserialize<MetadataTemplate>(File.ReadAllText(path), JsonFile.Options);
            return template is { Name.Length: > 0, Fields: not null }
                ? template
                : throw new InvalidDataException("The file isn't a PictureIt template.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The file isn't a PictureIt template.", ex);
        }
    }
}
