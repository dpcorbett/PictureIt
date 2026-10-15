using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using PictureIt.Core.Catalog;
using PictureIt.Core.Model;
using PictureIt.Presentation.Services;

namespace PictureIt.App.Services;

public sealed class DialogService(WindowContext window) : IDialogService
{
    public async Task<bool> ConfirmChangesAsync(ChangePreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var file = preview.Files.Count == 1 ? Path.GetFileName(preview.Files[0].FilePath) : $"{preview.Files.Count} photos";

        var changes = new StackPanel { Spacing = 12 };
        foreach (var change in preview.Files.SelectMany(f => f.Changes))
        {
            changes.Children.Add(ChangeRow(change));
        }

        var dialog = Create(
            $"Save changes to {file}?",
            new ScrollViewer { Content = changes, MaxHeight = 420 });
        dialog.PrimaryButtonText = "Save";
        dialog.CloseButtonText = "Cancel";
        dialog.DefaultButton = ContentDialogButton.Primary;

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task<UnsavedChangesChoice> AskToSaveChangesAsync(string fileName)
    {
        var dialog = Create("Unsaved changes", new TextBlock { Text = $"Save your changes to {fileName}?", TextWrapping = TextWrapping.Wrap });
        dialog.PrimaryButtonText = "Save";
        dialog.SecondaryButtonText = "Don't save";
        dialog.CloseButtonText = "Cancel";
        dialog.DefaultButton = ContentDialogButton.Primary;

        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => UnsavedChangesChoice.Save,
            ContentDialogResult.Secondary => UnsavedChangesChoice.Discard,
            _ => UnsavedChangesChoice.Cancel,
        };
    }

    public async Task ShowErrorAsync(string title, string message)
    {
        var dialog = Create(title, new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        dialog.CloseButtonText = "OK";
        await dialog.ShowAsync();
    }

    private ContentDialog Create(string title, object content) => new()
    {
        Title = title,
        Content = content,
        XamlRoot = window.XamlRoot,
        Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        RequestedTheme = (window.RequiredWindow.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default,
    };

    private static StackPanel ChangeRow(TagChange change)
    {
        var name = TagCatalog.FindCommonField(change.Key)?.DisplayName ?? change.Key.ToString();
        var secondary = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

        var values = new TextBlock { TextWrapping = TextWrapping.Wrap };
        if (change.OldValue is not null)
        {
            values.Inlines.Add(new Run { Text = change.OldValue, Foreground = secondary, TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough });
            values.Inlines.Add(new Run { Text = "  →  ", Foreground = secondary });
        }

        values.Inlines.Add(change.NewValue is null
            ? new Run { Text = "(removed)", FontStyle = Windows.UI.Text.FontStyle.Italic, Foreground = secondary }
            : new Run { Text = change.NewValue });

        return new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = name, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] },
                values,
            },
        };
    }
}
