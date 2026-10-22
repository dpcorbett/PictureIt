using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PictureIt.Core.Catalog;
using PictureIt.Presentation.ViewModels;

namespace PictureIt.App.Views;

/// <summary>Picks the editor row template for a tag: read-only text, a text box, a multi-line box or stars.</summary>
public sealed partial class TagFieldTemplateSelector : DataTemplateSelector
{
    public DataTemplate? ReadOnly { get; set; }

    public DataTemplate? Text { get; set; }

    public DataTemplate? Multiline { get; set; }

    public DataTemplate? Rating { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item switch
    {
        TagFieldViewModel { IsReadOnly: true } => ReadOnly,
        TagFieldViewModel { Editor: EditorKind.MultilineText } => Multiline,
        TagFieldViewModel { Editor: EditorKind.Rating } => Rating,
        _ => Text,
    };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}
