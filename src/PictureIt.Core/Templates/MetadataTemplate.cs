using PictureIt.Core.Catalog;
using PictureIt.Core.Model;

namespace PictureIt.Core.Templates;

/// <summary>
/// One field of a template. List fields can add to what's there (keywords) instead of replacing it.
/// </summary>
public sealed record TemplateField(TagKey Key, bool AddToList, IReadOnlyList<string> Values)
{
    public MetadataEdit ToEdit() => AddToList ? MetadataEdit.AddToList(Key, Values) : MetadataEdit.Set(Key, Values);
}

/// <summary>A named set of values to stamp onto photos, e.g. "Copyright 2026, Dave Corbett".</summary>
public sealed record MetadataTemplate(string Name, IReadOnlyList<TemplateField> Fields)
{
    public IReadOnlyList<MetadataEdit> ToEdits() => [.. Fields.Select(f => f.ToEdit())];

    /// <summary>
    /// A template holding the photo's non-empty common fields (except date taken, which is specific to each photo).
    /// List fields add to the targets' lists rather than replacing them.
    /// </summary>
    public static MetadataTemplate FromPhoto(string name, ImageMetadata photo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(photo);

        var fields = TagCatalog.CommonFields
            .Where(f => f.Editor != EditorKind.DateTime)
            .Where(f => photo.TryGetTag(f.Key, out var tag) && tag.Values.Any(v => v.Length > 0))
            .Select(f =>
            {
                photo.TryGetTag(f.Key, out var tag);
                return new TemplateField(f.Key, AddToList: f.Editor == EditorKind.List, tag.Values);
            })
            .ToList();

        return new MetadataTemplate(name.Trim(), fields);
    }
}

public interface ITemplateStore
{
    IReadOnlyList<MetadataTemplate> Load();

    void Save(IReadOnlyList<MetadataTemplate> templates);

    /// <summary>Writes one template to a file for sharing.</summary>
    void Export(MetadataTemplate metadataTemplate, string path);

    /// <summary>Reads a shared template file. Throws <see cref="InvalidDataException"/> if it isn't one.</summary>
    MetadataTemplate Import(string path);
}
