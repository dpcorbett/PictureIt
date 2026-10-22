using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PictureIt.App.Dialogs;
using PictureIt.Core.Model;
using PictureIt.Presentation.Services;
using PictureIt.Presentation.ViewModels.Tools;

namespace PictureIt.App.Services;

public sealed class DialogService(WindowContext window) : IDialogService
{
    public async Task<bool> ConfirmChangesAsync(ChangePreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var target = preview.ChangedFileCount == 1
            ? Path.GetFileName(preview.Files.First(f => f.Changes.Count > 0).FilePath)
            : $"{preview.ChangedFileCount} photos";

        var dialog = Create($"Save changes to {target}?", new ChangePreviewView(preview));
        dialog.PrimaryButtonText = "Save";
        dialog.CloseButtonText = "Cancel";
        dialog.DefaultButton = ContentDialogButton.Primary;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task<UnsavedChangesChoice> AskToSaveChangesAsync(string fileName)
    {
        var dialog = Create("Unsaved changes", Text($"Save your changes to {fileName}?"));
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

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        var dialog = Create(title, Text(message));
        dialog.PrimaryButtonText = confirmText;
        dialog.CloseButtonText = "Cancel";
        dialog.DefaultButton = ContentDialogButton.Close;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task ShowErrorAsync(string title, string message)
    {
        var dialog = Create(title, Text(message));
        dialog.CloseButtonText = "OK";
        await dialog.ShowAsync();
    }

    public Task ShowInfoAsync(string title, string message) => ShowErrorAsync(title, message);

    public async Task ShowWriteFailuresAsync(WriteResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var failures = new StackPanel { Spacing = 10 };
        foreach (var file in result.Files.Where(f => !f.Succeeded))
        {
            failures.Children.Add(new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = Path.GetFileName(file.FilePath), Style = Resource<Style>("BodyStrongTextBlockStyle") },
                    Text(file.Error ?? "Unknown error"),
                },
            });
        }

        var content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                Text($"{result.SucceededCount} of {result.Files.Count} photos were changed. These weren't:"),
                new ScrollViewer { Content = failures, MaxHeight = 360 },
            },
        };

        var dialog = Create("Some photos weren't changed", content);
        dialog.CloseButtonText = "OK";
        await dialog.ShowAsync();
    }

    public async Task<string?> PromptAsync(string title, string label, string initialText)
    {
        var box = new TextBox { Header = label, Text = initialText, Width = 360 };
        box.SelectAll();
        var dialog = Create(title, box);
        dialog.PrimaryButtonText = "OK";
        dialog.CloseButtonText = "Cancel";
        dialog.DefaultButton = ContentDialogButton.Primary;
        box.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = box.Text.Trim().Length > 0;
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? box.Text.Trim() : null;
    }

    public async Task<bool> ShowToolAsync(ToolViewModel tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        UIElement content = tool switch
        {
            FindReplaceToolViewModel findReplace => new FindReplaceView(findReplace),
            ShiftDatesToolViewModel shiftDates => new ShiftDatesView(shiftDates),
            RemoveMetadataToolViewModel remove => new RemoveMetadataView(remove),
            PasteMetadataToolViewModel paste => new PasteMetadataView(paste),
            RenameToolViewModel rename => new RenameView(rename),
            SortToolViewModel sort => new SortView(sort),
            ExportToolViewModel export => new ExportView(export),
            FindDuplicatesToolViewModel findDuplicates => new FindDuplicatesView(findDuplicates),
            DuplicatesViewModel duplicates => new DuplicatesView(duplicates),
            _ => throw new ArgumentException($"No view for {tool.GetType().Name}.", nameof(tool)),
        };

        var dialog = Create(tool.Title, content);
        dialog.PrimaryButtonText = tool.ConfirmText;
        dialog.CloseButtonText = "Cancel";
        dialog.DefaultButton = ContentDialogButton.Primary;
        dialog.IsPrimaryButtonEnabled = tool.CanApply;

        // The button text can change too ("Rename 12 photos").
        void OnToolChanged(object? sender, PropertyChangedEventArgs e)
        {
            dialog.IsPrimaryButtonEnabled = tool.CanApply;
            dialog.PrimaryButtonText = tool.ConfirmText;
        }

        tool.PropertyChanged += OnToolChanged;
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            tool.PropertyChanged -= OnToolChanged;
        }
    }

    public async Task<T> RunWithProgressAsync<T>(string title, Func<IProgress<WriteProgress>, CancellationToken, Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        using var cancellation = new CancellationTokenSource();
        var bar = new ProgressBar { IsIndeterminate = true, Width = 420 };
        var status = new TextBlock { Text = "Starting…", TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 420 };
        var dialog = Create(title, new StackPanel { Spacing = 12, Children = { status, bar } });
        dialog.CloseButtonText = "Cancel";

        // Cancel asks the work to stop after the current photo; the dialog closes when the work does.
        dialog.CloseButtonClick += (_, args) =>
        {
            args.Cancel = true;
            status.Text = "Stopping after the current photo…";
            cancellation.Cancel();
        };

        // Progress<T> reports on the UI thread, since it's created here.
        var progress = new Progress<WriteProgress>(p =>
        {
            bar.IsIndeterminate = false;
            bar.Maximum = Math.Max(1, p.Total);
            bar.Value = p.Completed;
            if (!cancellation.IsCancellationRequested)
            {
                status.Text = p.Completed < p.Total ? $"{p.Completed + 1} of {p.Total}: {Path.GetFileName(p.CurrentFile)}" : "Finishing…";
            }
        });

        var shown = dialog.ShowAsync();
        try
        {
            return await work(progress, cancellation.Token);
        }
        finally
        {
            dialog.Hide();
            await shown;
        }
    }

    private ContentDialog Create(string title, object content) => new()
    {
        Title = title,
        Content = content,
        XamlRoot = window.XamlRoot,
        Style = Resource<Style>("DefaultContentDialogStyle"),
        RequestedTheme = (window.RequiredWindow.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default,
    };

    private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };

    private static T Resource<T>(string key) => (T)Application.Current.Resources[key];
}
