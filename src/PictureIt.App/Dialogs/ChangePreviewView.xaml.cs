using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PictureIt.Core.Catalog;
using PictureIt.Core.Model;
using Windows.UI.Text;

namespace PictureIt.App.Dialogs;

/// <summary>One row of the change preview: a field on a photo, before and after.</summary>
public sealed class PreviewRow(string fileName, TagChange change)
{
    public string FileName { get; } = fileName;

    public string Field { get; } = TagCatalog.FindCommonField(change.Key)?.DisplayName ?? change.Key.Name;

    public string FieldKey { get; } = change.Key.ToString();

    public string OldText { get; } = change.OldValue ?? string.Empty;

    public Visibility HasOld => change.OldValue is null ? Visibility.Collapsed : Visibility.Visible;

    public string NewText { get; } = change.NewValue ?? "(removed)";

    public FontStyle NewStyle => change.NewValue is null ? FontStyle.Italic : FontStyle.Normal;

    public bool Matches(string filter) =>
        FileName.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
        || Field.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
        || FieldKey.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || OldText.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
        || NewText.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
}

/// <summary>Every change a batch will make, as a filterable table, with totals.</summary>
public sealed partial class ChangePreviewView : UserControl
{
    private readonly List<PreviewRow> _rows;

    public ChangePreviewView(ChangePreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        InitializeComponent();

        _rows = [.. preview.Files.SelectMany(f => f.Changes.Select(c => new PreviewRow(Path.GetFileName(f.FilePath), c)))];
        Rows.ItemsSource = _rows;

        var photos = preview.ChangedFileCount == 1 ? "1 photo" : $"{preview.ChangedFileCount} photos";
        var changes = preview.ChangeCount == 1 ? "1 change" : $"{preview.ChangeCount} changes";
        var unchanged = preview.Files.Count - preview.ChangedFileCount;
        SummaryText.Text = $"{changes} to {photos}."
            + (unchanged > 0 ? $" {unchanged} selected {(unchanged == 1 ? "photo already matches" : "photos already match")} and won't be touched." : string.Empty)
            + " Each photo is backed up first, so this can be undone from History.";

        // A filter is only worth showing when there's a lot to look through.
        FilterBox.Visibility = _rows.Count > 8 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnFilterChanged(object sender, TextChangedEventArgs e)
    {
        var filter = FilterBox.Text.Trim();
        Rows.ItemsSource = filter.Length == 0 ? _rows : _rows.Where(r => r.Matches(filter)).ToList();
    }
}
