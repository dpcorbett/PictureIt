using System.Collections.ObjectModel;
using PictureIt.Core.Catalog;

namespace PictureIt.Presentation.ViewModels;

/// <summary>A section of the editor, e.g. "Camera". Holds the fields that pass the current filter.</summary>
public sealed class TagGroupViewModel(TagCategory category, IEnumerable<TagFieldViewModel> fields)
{
    public TagCategory Category { get; } = category;

    public string Title => Category switch
    {
        TagCategory.Common => "Common fields",
        TagCategory.MakerNotes => "Maker notes",
        _ => Category.ToString(),
    };

    public ObservableCollection<TagFieldViewModel> Fields { get; } = [.. fields];
}
