using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PictureIt.Core.Catalog;

namespace PictureIt.Presentation.ViewModels;

/// <summary>
/// A section of the editor, e.g. "Camera". <see cref="Items"/> are the fields that pass the current filter;
/// <see cref="Fields"/> are the ones on screen, which is none while the section is collapsed.
/// </summary>
public sealed partial class TagGroupViewModel : ObservableObject
{
    private readonly Action<TagGroupViewModel>? _expandedChanged;

    public TagGroupViewModel(TagCategory category, IEnumerable<TagFieldViewModel> items, bool isExpanded = true, Action<TagGroupViewModel>? expandedChanged = null)
    {
        Category = category;
        Items = [.. items];
        IsExpanded = isExpanded;
        Fields = isExpanded ? [.. Items] : [];
        _expandedChanged = expandedChanged;
    }

    public TagCategory Category { get; }

    public string Title => Category switch
    {
        TagCategory.Common => "Common fields",
        TagCategory.MakerNotes => "Maker notes",
        _ => Category.ToString(),
    };

    public IReadOnlyList<TagFieldViewModel> Items { get; }

    /// <summary>The fields on screen: all of <see cref="Items"/> when expanded, none when collapsed.</summary>
    public ObservableCollection<TagFieldViewModel> Fields { get; }

    /// <summary>How many fields the section holds, shown in its header.</summary>
    public string CountText => Items.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>Accessible name for the header button, e.g. "Camera, 24 tags, collapsed".</summary>
    public string HeaderLabel => $"{Title}, {Items.Count} {(Items.Count == 1 ? "tag" : "tags")}, {(IsExpanded ? "expanded" : "collapsed")}";

    /// <summary>Chevron: down when expanded, right when collapsed.</summary>
    public string Chevron => IsExpanded ? "" : "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Chevron), nameof(HeaderLabel))]
    public partial bool IsExpanded { get; set; }

    [RelayCommand]
    private void Toggle() => IsExpanded = !IsExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        // Fields isn't set yet while the constructor sets IsExpanded.
        if (Fields is null)
        {
            return;
        }

        Fields.Clear();
        if (value)
        {
            foreach (var field in Items)
            {
                Fields.Add(field);
            }
        }

        _expandedChanged?.Invoke(this);
    }
}
